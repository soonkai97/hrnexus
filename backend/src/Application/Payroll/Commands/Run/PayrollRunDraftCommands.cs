using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Commands.Run;

/// <summary>Starts the payroll run for a month: everyone who works in it, calculated from what the database says now.</summary>
public record StartPayrollRunCommand(int Year, int Month, PayrollActor By) : IRequest<Guid>;

public class StartPayrollRunCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<StartPayrollRunCommand, Guid>
{
    public async Task<Guid> Handle(StartPayrollRunCommand req, CancellationToken ct)
    {
        if (req.Month is < 1 or > 12 || req.Year is < 2000 or > 2200)
            throw new ArgumentException("The payroll month is not a valid month.");
        var index = req.Year * 12 + req.Month;
        var local = clock.LocalNow;
        if (index > local.Year * 12 + local.Month + 1)
            throw new ArgumentException("A payroll run can only be started for this month, an earlier month or next month.");

        return await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            var now = clock.UtcNow;
            if (await db.PayrollRuns.AnyAsync(r => r.Year == req.Year && r.Month == req.Month, c))
                throw new InvalidOperationException($"A payroll run for {req.Year}-{req.Month:D2} already exists.");

            // At most one run is open at a time, whatever its month, so two runs can never deduct the same cash advance.
            var open = await db.PayrollRuns.Where(r => r.State != PayrollRunState.Locked)
                .OrderBy(r => r.Year).ThenBy(r => r.Month).Select(r => new { r.Year, r.Month }).FirstOrDefaultAsync(c);
            if (open is not null)
                throw new InvalidOperationException(
                    $"The {open.Year}-{open.Month:D2} payroll run is not locked yet. Finish it before starting {req.Year}-{req.Month:D2}.");

            var loaded = await PayrollRunInputs.LoadAsync(db, req.Year, req.Month, c);
            if (loaded.People.Count == 0)
                throw new InvalidOperationException("Nobody has a salary and employment in this month yet, so there is nothing to run.");

            var run = PayrollRun.Start(req.Year, req.Month, req.By.Id, PayrollRunWork.BasisOf(loaded), now);
            foreach (var person in loaded.People)
            {
                run.AddLine(PayrollRunLine.Create(person.EmployeeId, person.StaffId, person.Name), req.By.Id, now);
                run.ApplyResult(person.EmployeeId, PayrollRunCalculator.Calculate(person, loaded.Month), req.By.Id, now);
            }
            db.PayrollRuns.Add(run);
            // A concurrent start of the same month conflicts under Serializable and is retried by the strategy, which
            // then finds the run above; any other database error is not disguised as "already exists".
            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Started", req.By, c);
            return run.Id;
        }, ct);
    }
}

/// <summary>Sets one person's hand-entered inputs (the PCB, or leaving them out with a reason) and calculates them again. Returns the run's new version.</summary>
public record UpdatePayrollRunLineCommand(Guid RunId, Guid EmployeeId, decimal? Pcb, bool Excluded, string? ExcludedReason,
    long ExpectedVersion, PayrollActor By) : IRequest<long>;

public class UpdatePayrollRunLineCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<UpdatePayrollRunLineCommand, long>
{
    public async Task<long> Handle(UpdatePayrollRunLineCommand req, CancellationToken ct)
    {
        if (req.Pcb is { } pcb && (pcb < 0m || pcb > AppConstants.Payroll.MaxMonthlySalary || decimal.Round(pcb, 2) != pcb))
            throw new ArgumentException("The PCB must be an amount of zero or more with at most two decimal places.");

        return await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            var now = clock.UtcNow;
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            PayrollRunWork.EnsureVersion(run, req.ExpectedVersion);

            run.SetLineInputs(req.EmployeeId, req.Pcb, req.Excluded, req.ExcludedReason, req.By.Id, now);
            var line = run.LineFor(req.EmployeeId);
            if (!req.Excluded)
            {
                var loaded = await PayrollRunInputs.LoadAsync(db, run.Year, run.Month, c);
                var person = loaded.People.FirstOrDefault(p => p.EmployeeId == req.EmployeeId)
                    ?? throw new InvalidOperationException($"{line.StaffId} can no longer be calculated (no salary or employment this month). Leave them out of the run first.");
                run.ApplyResult(req.EmployeeId, PayrollRunCalculator.Calculate(person with { Pcb = req.Pcb }, loaded.Month), req.By.Id, now);
            }

            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "LineEdited", req.By, c, $" {line.StaffId}");
            return run.Version;
        }, ct);
    }
}

/// <summary>Calculates everyone in a draft run again (leave, salary and settings may have changed) and adds people who have become eligible. Returns the new version.</summary>
public record RecalculatePayrollRunCommand(Guid RunId, long ExpectedVersion, PayrollActor By) : IRequest<long>;

public class RecalculatePayrollRunCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<RecalculatePayrollRunCommand, long>
{
    public async Task<long> Handle(RecalculatePayrollRunCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            PayrollRunWork.EnsureVersion(run, req.ExpectedVersion);
            await PayrollRunWork.RecalculateAsync(db, run, req.By, clock.UtcNow, addNewPeople: true, c);
            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Recalculated", req.By, c);
            return run.Version;
        }, ct);
}

/// <summary>Deletes a draft run (nothing has been approved or deducted).</summary>
public record DeletePayrollRunCommand(Guid RunId, PayrollActor By) : IRequest;

public class DeletePayrollRunCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<DeletePayrollRunCommand>
{
    public async Task Handle(DeletePayrollRunCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            run.EnsureCanDelete();
            db.PayrollRuns.Remove(run);
            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Deleted", req.By, c);
            return 0;
        }, ct);
}
