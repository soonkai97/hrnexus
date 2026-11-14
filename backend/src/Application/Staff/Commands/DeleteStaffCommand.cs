using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using MediatR;

namespace HRNexus.Application.Staff.Commands;

public record DeleteStaffCommand(Guid Id) : IRequest;

public class DeleteStaffCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteStaffCommand>
{
    public async Task Handle(DeleteStaffCommand req, CancellationToken ct)
    {
        var staff = await db.Staff.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Staff {req.Id} not found.");

        // Lockout guard: deleting is as final as deactivating. The last active person who can manage roles
        // and permissions cannot be deleted (protection is by permission, whatever the role is called).
        await RolesManageInvariant.EnsureNotTheLastAsync(db, req.Id, "deleting", ct);

        // Delete is a soft-delete of the Staff row (Remove → IsDeleted=true), so DB cascade does NOT
        // fire — every related record must be removed explicitly, or the person's roles/leave
        // would be orphaned behind them.
        db.StaffRoles              .RemoveRange(db.StaffRoles              .Where(r => r.EmployeeId == req.Id));  // assigned roles
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
