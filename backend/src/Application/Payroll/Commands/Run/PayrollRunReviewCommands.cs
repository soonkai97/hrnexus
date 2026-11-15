using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Commands.Run;

/// <summary>Calculates the run once more inside the transaction and freezes it for review. Returns the new version.</summary>
public record SubmitPayrollRunCommand(Guid RunId, long ExpectedVersion, PayrollActor By) : IRequest<long>;

public class SubmitPayrollRunCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<SubmitPayrollRunCommand, long>
{
    public async Task<long> Handle(SubmitPayrollRunCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            var now = clock.UtcNow;
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            PayrollRunWork.EnsureVersion(run, req.ExpectedVersion);

            // What the approver sees must be what was calculated at this moment, never a stale draft.
            var missing = await PayrollRunWork.RecalculateAsync(db, run, req.By, now, addNewPeople: false, c);
            if (missing.Count > 0)
                throw new InvalidOperationException("Someone has become eligible for this month since the run was last calculated. Recalculate the run before submitting it.");

            run.Submit(req.By.Id, now);
            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Submitted", req.By, c);
            return run.Version;
        }, ct);
}

/// <summary>Sends a submitted or approved run back to draft with a reason. Head of HR only; every sign-off is cleared.</summary>
public record ReturnPayrollRunCommand(Guid RunId, string Reason, long ExpectedVersion, PayrollActor By) : IRequest<long>;

public class ReturnPayrollRunCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<ReturnPayrollRunCommand, long>
{
    public async Task<long> Handle(ReturnPayrollRunCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequireApproverAsync(db, req.By, c);
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            PayrollRunWork.EnsureVersion(run, req.ExpectedVersion);
            run.Return(req.By.Id, req.Reason, clock.UtcNow);
            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Returned", req.By, c);   // the reason is in the run's history, not the audit row
            return run.Version;
        }, ct);
}

/// <summary>
/// Signs off one person's line. Allowed for that person's reporting manager (who holds payroll.signoff) or for a head
/// of HR (payroll.approve); nobody signs their own line and nobody who prepared the run signs (the run refuses both).
/// </summary>
public record SignOffPayrollLineCommand(Guid RunId, Guid EmployeeId, long ExpectedVersion, PayrollActor By) : IRequest<long>;

public class SignOffPayrollLineCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<SignOffPayrollLineCommand, long>
{
    public async Task<long> Handle(SignOffPayrollLineCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            PayrollRunWork.EnsureVersion(run, req.ExpectedVersion);   // nobody signs figures that changed since they looked
            var line = run.LineFor(req.EmployeeId);

            var approvers = await PermissionHolders.GetIdsAsync(db, AppConstants.Permissions.PayrollApprove, c);
            if (!approvers.Contains(req.By.Id))
            {
                var reportsTo = await db.Staff.Where(s => s.Id == req.EmployeeId).Select(s => s.ReportsToId).FirstOrDefaultAsync(c);
                var signers = await PermissionHolders.GetIdsAsync(db, AppConstants.Permissions.PayrollSignoff, c);
                if (reportsTo != req.By.Id || !signers.Contains(req.By.Id))
                    throw new ForbiddenException("Only this person's reporting manager or the head of HR can sign off their pay.");
            }

            run.SignOff(req.EmployeeId, req.By.Id, clock.UtcNow);
            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "SignedOff", req.By, c, $" {line.StaffId}");
            return run.Version;
        }, ct);
}

/// <summary>A head of HR signs off every line still waiting, except their own. Returns how many were signed.</summary>
public record SignOffAllPayrollLinesCommand(Guid RunId, long ExpectedVersion, PayrollActor By) : IRequest<int>;

public class SignOffAllPayrollLinesCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<SignOffAllPayrollLinesCommand, int>
{
    public async Task<int> Handle(SignOffAllPayrollLinesCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequireApproverAsync(db, req.By, c);
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            PayrollRunWork.EnsureVersion(run, req.ExpectedVersion);
            if (run.State != PayrollRunState.Submitted)
                throw new InvalidOperationException("Only a submitted run can be signed off.");
            var waiting = run.Lines.Where(l => !l.Excluded && !l.IsSignedOff && l.EmployeeId != req.By.Id).Select(l => l.EmployeeId).ToList();
            var now = clock.UtcNow;
            foreach (var id in waiting) run.SignOff(id, req.By.Id, now);
            if (waiting.Count > 0)
            {
                await db.SaveChangesAsync(c);
                await PayrollRunWork.AuditAsync(audit, run, "SignedOffAll", req.By, c, $" {waiting.Count} lines");
            }
            return waiting.Count;
        }, ct);
}

/// <summary>
/// Approves the run once every line is signed. Head of HR only. Refused while any statutory rate the frozen figures
/// used is not verified by the accountant, and (by the run) for whoever prepared it or whose own pay is in it.
/// </summary>
public record ApprovePayrollRunCommand(Guid RunId, long ExpectedVersion, PayrollActor By) : IRequest<long>;

public class ApprovePayrollRunCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<ApprovePayrollRunCommand, long>
{
    public async Task<long> Handle(ApprovePayrollRunCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequireApproverAsync(db, req.By, c);
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            PayrollRunWork.EnsureVersion(run, req.ExpectedVersion);

            // The rate rows the frozen lines really used (their stored rate ids), not whatever is in force today.
            var usedRates = run.Lines.Where(l => !l.Excluded).SelectMany(l => l.Items)
                .Where(i => i.RateRuleId != null).Select(i => i.RateRuleId!.Value).Distinct().ToList();
            var unverified = await db.StatutoryRateTables.Where(t => usedRates.Contains(t.Id) && !t.Verified)
                .Select(t => new { t.Scheme, t.CitizenshipClass }).ToListAsync(c);
            if (unverified.Count > 0)
                throw new InvalidOperationException("These statutory rates have not been verified by the accountant yet: " +
                    string.Join(", ", unverified.Select(u => $"{u.Scheme.ToUpperInvariant()} ({u.CitizenshipClass})").Distinct().Order()) + ".");

            run.Approve(req.By.Id, clock.UtcNow);
            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Approved", req.By, c);
            return run.Version;
        }, ct);
}
