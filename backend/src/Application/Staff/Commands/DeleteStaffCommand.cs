using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Commands;

public record DeleteStaffCommand(Guid Id) : IRequest;

public class DeleteStaffCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteStaffCommand>
{
    public async Task Handle(DeleteStaffCommand req, CancellationToken ct)
    {
        var staff = await db.Staff.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Staff {req.Id} not found.");

        // Protect data integrity: staff with shift history should be deactivated, not deleted
        var hasHistory = await db.WorkLogs.AnyAsync(l => l.EmployeeId == req.Id, ct);
        if (hasHistory)
            throw new InvalidOperationException(
                "This staff member has shift history. Deactivate them instead to preserve payroll records.");

        // Delete is a soft-delete of the Staff row (Remove → IsDeleted=true), so DB cascade does NOT
        // fire — every related record must be removed explicitly, or the person's jobs/locations/leave
        // would be orphaned behind them. WorkLogs are never present here (the guard above blocks
        // deleting anyone with shift history — deactivate instead to keep payroll records).
        db.StaffRoles              .RemoveRange(db.StaffRoles              .Where(r => r.EmployeeId == req.Id));  // assigned jobs
        db.StaffLocations          .RemoveRange(db.StaffLocations          .Where(l => l.EmployeeId == req.Id));  // assigned locations
        db.LeaveRequests           .RemoveRange(db.LeaveRequests           .Where(l => l.EmployeeId == req.Id));
        db.LeaveEntitlements       .RemoveRange(db.LeaveEntitlements       .Where(l => l.EmployeeId == req.Id));
        db.CashAdvances            .RemoveRange(db.CashAdvances            .Where(c => c.EmployeeId == req.Id));
        db.StaffPermissionOverrides.RemoveRange(db.StaffPermissionOverrides.Where(o => o.EmployeeId == req.Id));
        db.Notifications           .RemoveRange(db.Notifications           .Where(n => n.EmployeeId == req.Id));
        db.PushSubscriptions       .RemoveRange(db.PushSubscriptions       .Where(p => p.EmployeeId == req.Id));

        db.Staff.Remove(staff);
        await db.SaveChangesAsync(ct);
    }
}
