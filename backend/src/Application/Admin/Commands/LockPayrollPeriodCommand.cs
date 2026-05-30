using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Admin.Commands;

/// <summary>Locks a payroll period (one calendar month). Manager/admin only (enforced at the API).</summary>
public record LockPayrollPeriodCommand(int Year, int Month, string PerformedByName, string? Note = null)
    : IRequest<Guid>;

public class LockPayrollPeriodCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<LockPayrollPeriodCommand, Guid>
{
    public async Task<Guid> Handle(LockPayrollPeriodCommand req, CancellationToken ct)
    {
        if (req.Month is < 1 or > 12)
            throw new ArgumentException("Month must be between 1 and 12.");

        if (await db.PayrollLocks.AnyAsync(p => p.Year == req.Year && p.Month == req.Month, ct))
            throw new InvalidOperationException("This payroll period is already locked.");

        var lockRow = PayrollLock.Create(req.Year, req.Month, req.PerformedByName, req.Note);
        db.PayrollLocks.Add(lockRow);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index on (Year, Month) — a concurrent lock slipped past the AnyAsync check.
            throw new InvalidOperationException("This payroll period is already locked.");
        }

        await audit.LogAsync("PayrollLock", lockRow.Id.ToString(), "Locked",
            null, req.PerformedByName, $"Locked {req.Year}-{req.Month:D2}", ct);

        return lockRow.Id;
    }
}
