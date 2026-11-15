using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using MediatR;
using Microsoft.EntityFrameworkCore;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Payroll.Commands.Run;

/// <summary>
/// Locks an approved run: the month is paid out. Writes a repayment for every cash advance from the amount the frozen
/// payslip lines really took (never from the schedule), settles an advance exactly when its repayments reach what was
/// lent, and creates (or adopts) the month's payroll lock row so the older lock guards agree.
/// </summary>
public record LockPayrollRunCommand(Guid RunId, PayrollActor By) : IRequest;

public class LockPayrollRunCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<LockPayrollRunCommand>
{
    public async Task Handle(LockPayrollRunCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequireHolderAsync(db, AppConstants.Permissions.PayrollLock, req.By, "You are not allowed to lock payroll.", c);
            var now = clock.UtcNow;
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            run.Lock(req.By.Id, now);

            // What each advance was really charged: the applied amount of the lines that name it.
            var applied = run.Lines.Where(l => !l.Excluded).SelectMany(l => l.Items)
                .Where(i => i.Section == LineSection.OtherDeduction && i.SourceId != null && i.Amount > 0m)
                .GroupBy(i => i.SourceId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));
            var ids = applied.Keys.ToList();
            var advances = await db.CashAdvances.Where(a => ids.Contains(a.Id)).ToListAsync(c);   // pay-item ids are not advances
            // Repayment amounts are encrypted, so they are summed here and not in SQL.
            var earlier = (await db.CashAdvanceRepayments.Where(r => ids.Contains(r.CashAdvanceId)).ToListAsync(c))
                .GroupBy(r => r.CashAdvanceId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            // Every id a payslip deduction names must still be something real: an advance that is still outstanding, or a
            // pay item. Anything else was deleted or settled by hand after the run was submitted, and locking would then
            // record a deduction against nothing.
            var otherIds = ids.Except(advances.Select(a => a.Id)).ToList();
            if (otherIds.Count > 0 && await db.PayItems.CountAsync(i => otherIds.Contains(i.Id), c) != otherIds.Count)
                throw new InvalidOperationException("A deduction in this run names a cash advance or pay item that no longer exists. Return the run and recalculate it.");
            if (advances.Any(a => a.Status != E.CashAdvance.Outstanding))
                throw new InvalidOperationException("A cash advance in this run was settled outside it. Return the run and recalculate it.");

            foreach (var advance in advances)
            {
                // Never record more than was lent: two runs could otherwise both deduct the same advance.
                var total = earlier.GetValueOrDefault(advance.Id) + applied[advance.Id];
                if (total > advance.Amount)
                    throw new InvalidOperationException("Locking would repay a cash advance more than was lent. Return the run and recalculate it.");
                db.CashAdvanceRepayments.Add(CashAdvanceRepayment.Create(advance.Id, run.Id, applied[advance.Id], now));
                if (total >= advance.Amount) advance.MarkSettled();
            }

            if (!await db.PayrollLocks.AnyAsync(l => l.Year == run.Year && l.Month == run.Month, c))
                db.PayrollLocks.Add(PayrollLock.Create(run.Year, run.Month, req.By.Name, "Payroll run"));

            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Locked", req.By, c);
            return 0;
        }, ct);
}

/// <summary>
/// Reopens a locked run for correction: back to draft, the month's lock row removed, this run's repayments deleted and
/// any advance they had settled put back to outstanding. Refused once payslips or a bank file were produced, or when
/// a later month's run has moved past draft (it already relied on this month).
/// </summary>
public record ReopenPayrollRunCommand(Guid RunId, PayrollActor By) : IRequest;

public class ReopenPayrollRunCommandHandler(IAppDbContext db, IAuditService audit, IDateTimeProvider clock)
    : IRequestHandler<ReopenPayrollRunCommand>
{
    public async Task Handle(ReopenPayrollRunCommand req, CancellationToken ct) =>
        await PayrollRunWork.InTransactionAsync(db, async c =>
        {
            await PayrollRunWork.RequireHolderAsync(db, AppConstants.Permissions.PayrollReopen, req.By, "You are not allowed to reopen payroll.", c);
            var run = await PayrollRunWork.LoadAsync(db, req.RunId, c);
            var index = run.Year * 12 + run.Month;
            // A later run (in any state) was calculated on top of this month's repayments; reopening this one would let both deduct the same advance.
            if (await db.PayrollRuns.AnyAsync(r => r.Year * 12 + r.Month > index, c))
                throw new InvalidOperationException("A later month's payroll run exists, so this month cannot be reopened.");
            run.Reopen(req.By.Id, clock.UtcNow);

            var lockRow = await db.PayrollLocks.FirstOrDefaultAsync(l => l.Year == run.Year && l.Month == run.Month, c);
            if (lockRow is not null) db.PayrollLocks.Remove(lockRow);

            var repayments = await db.CashAdvanceRepayments.Where(r => r.RunId == run.Id).ToListAsync(c);
            if (repayments.Count > 0)
            {
                db.CashAdvanceRepayments.RemoveRange(repayments);
                var ids = repayments.Select(r => r.CashAdvanceId).Distinct().ToList();
                var otherRepaid = (await db.CashAdvanceRepayments.Where(r => ids.Contains(r.CashAdvanceId) && r.RunId != run.Id).ToListAsync(c))
                    .GroupBy(r => r.CashAdvanceId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
                foreach (var advance in await db.CashAdvances.Where(a => ids.Contains(a.Id)).ToListAsync(c))
                    if (advance.Status == E.CashAdvance.Settled && otherRepaid.GetValueOrDefault(advance.Id) < advance.Amount)
                        advance.Reopen();
            }

            await db.SaveChangesAsync(c);
            await PayrollRunWork.AuditAsync(audit, run, "Reopened", req.By, c);
            return 0;
        }, ct);
}
