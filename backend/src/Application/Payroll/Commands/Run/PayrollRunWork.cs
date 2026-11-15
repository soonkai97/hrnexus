using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Commands.Run;

/// <summary>Who is acting: the staff id the run's history records, and the name the audit row shows.</summary>
public sealed record PayrollActor(Guid Id, string Name);

/// <summary>What every payroll run command shares: the transaction, loading a run whole, the version check, recalculation, the approver check and the audit row.</summary>
internal static class PayrollRunWork
{
    /// <summary>
    /// One Serializable transaction inside the retry strategy, started from a clean change tracker. A stale
    /// write (someone changed the run since it was read) surfaces as a clear conflict instead of overwriting it.
    /// </summary>
    public static Task<T> InTransactionAsync<T>(IAppDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct) =>
        SerializableWork.RunAsync(db, work, ct);

    /// <summary>
    /// A run with everything it owns, tracked. Loaded as separate queries (the run and its lines and history, then each
    /// kind of line child) so one big join does not multiply every person's rows by their items and warnings.
    /// </summary>
    public static async Task<PayrollRun> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        var run = await db.PayrollRuns.Include(r => r.Actors).Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("Payroll run not found.");
        var lines = db.PayrollRuns.Where(r => r.Id == id).SelectMany(r => r.Lines);
        await lines.Include(l => l.Items).LoadAsync(ct);
        await lines.Include(l => l.UnpaidDays).LoadAsync(ct);
        await lines.Include(l => l.Warnings).LoadAsync(ct);
        return run;
    }

    public static void EnsureVersion(PayrollRun run, long expected)
    {
        if (run.Version != expected)
            throw new InvalidOperationException("The payroll run was changed by someone else. Refresh and try again.");
    }

    public static PayrollRunBasis BasisOf(PayrollRunPeople loaded) =>
        new(PayrollCalculator.Version, loaded.Month.Divisor, loaded.Month.Company.WorkdayHours, loaded.Month.DeductionCapRate, loaded.PolicyVersion);

    /// <summary>
    /// Calculates every person in the run again from what the database says now, keeping the hand-entered inputs,
    /// and records the settings used. People who have since become eligible are returned (and added when asked);
    /// a person in the run who can no longer be calculated stops the recalculation.
    /// </summary>
    public static async Task<List<PersonSource>> RecalculateAsync(IAppDbContext db, PayrollRun run, PayrollActor by, DateTime now,
        bool addNewPeople, CancellationToken ct)
    {
        var loaded = await PayrollRunInputs.LoadAsync(db, run.Year, run.Month, ct);
        run.UpdateBasis(BasisOf(loaded));
        var people = loaded.People.ToDictionary(p => p.EmployeeId);

        foreach (var line in run.Lines.Where(l => !l.Excluded).ToList())
        {
            if (!people.TryGetValue(line.EmployeeId, out var person))
                throw new InvalidOperationException(
                    $"{line.StaffId} can no longer be calculated (no salary or employment this month). Leave them out of the run first.");
            run.ApplyResult(line.EmployeeId, PayrollRunCalculator.Calculate(person with { Pcb = line.PcbAmount }, loaded.Month), by.Id, now);
        }

        var present = run.Lines.Select(l => l.EmployeeId).ToHashSet();
        var missing = loaded.People.Where(p => !present.Contains(p.EmployeeId)).ToList();
        if (addNewPeople)
            foreach (var person in missing)
            {
                run.AddLine(PayrollRunLine.Create(person.EmployeeId, person.StaffId, person.Name), by.Id, now);
                run.ApplyResult(person.EmployeeId, PayrollRunCalculator.Calculate(person, loaded.Month), by.Id, now);
            }
        return missing;
    }

    /// <summary>
    /// The caller holds the permission now (active account, role grant plus per-user grant minus per-user revoke).
    /// Checked here as well as at the API, so a command can never be driven around the policy. The API must build the
    /// <see cref="PayrollActor"/> from the signed-in user only, never from a request body.
    /// </summary>
    public static async Task RequireHolderAsync(IAppDbContext db, string permissionKey, PayrollActor by, string message, CancellationToken ct)
    {
        var holders = await PermissionHolders.GetIdsAsync(db, permissionKey, ct);
        if (!holders.Contains(by.Id)) throw new ForbiddenException(message);
    }

    public static Task RequireApproverAsync(IAppDbContext db, PayrollActor by, CancellationToken ct) =>
        RequireHolderAsync(db, AppConstants.Permissions.PayrollApprove, by, "Only the head of HR can do this.", ct);

    public static Task RequirePreparerAsync(IAppDbContext db, PayrollActor by, CancellationToken ct) =>
        RequireHolderAsync(db, AppConstants.Permissions.PayrollManage, by, "You are not allowed to prepare payroll.", ct);

    /// <summary>The audit row says THAT a step happened, which month and who: never an amount, a reason or a message.</summary>
    public static Task AuditAsync(IAuditService audit, PayrollRun run, string action, PayrollActor by, CancellationToken ct, string? detail = null) =>
        audit.LogAsync("PayrollRun", run.Id.ToString(), action, by.Id, by.Name, $"{run.Year}-{run.Month:D2}{detail}", ct);
}
