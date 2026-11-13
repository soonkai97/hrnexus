using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Commands;

/// <summary>Reopens (unlocks) a previously locked payroll period. Manager/admin only.</summary>
public record ReopenPayrollPeriodCommand(int Year, int Month, string PerformedByName) : IRequest;

public class ReopenPayrollPeriodCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<ReopenPayrollPeriodCommand>
{
    public async Task Handle(ReopenPayrollPeriodCommand req, CancellationToken ct)
    {
        var lockRow = await db.PayrollLocks
            .FirstOrDefaultAsync(p => p.Year == req.Year && p.Month == req.Month, ct)
            ?? throw new KeyNotFoundException("This payroll period is not locked.");

        db.PayrollLocks.Remove(lockRow);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("PayrollLock", lockRow.Id.ToString(), "Reopened",
            null, req.PerformedByName, $"Reopened {req.Year}-{req.Month:D2}", ct);
    }
}
