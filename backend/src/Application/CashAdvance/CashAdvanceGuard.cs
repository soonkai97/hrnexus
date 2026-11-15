using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.CashAdvance;

/// <summary>
/// Once payroll has taken money back for an advance, or a run past draft has scheduled it, the advance's status follows
/// the repayments and cannot be changed or removed by hand. Before that, the manual settle, reopen and delete still work.
/// </summary>
public static class CashAdvanceGuard
{
    public static async Task EnsureNotInPayrollAsync(IAppDbContext db, Guid advanceId, CancellationToken ct)
    {
        if (await db.CashAdvanceRepayments.AnyAsync(r => r.CashAdvanceId == advanceId, ct))
            throw new InvalidOperationException("Payroll has already deducted repayments for this cash advance, so it can no longer be changed here.");

        var inOpenRun = await db.PayrollRuns.Where(r => r.State != PayrollRunState.Draft)
            .SelectMany(r => r.Lines).SelectMany(l => l.Items)
            .AnyAsync(i => i.SourceId == advanceId, ct);
        if (inOpenRun)
            throw new InvalidOperationException("This cash advance is part of a payroll run that is already submitted, so it cannot be changed here.");
    }
}
