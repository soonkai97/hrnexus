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

        // Explicitly remove all related records (no cascade configured for these tables)
        db.LeaveRequests    .RemoveRange(db.LeaveRequests    .Where(l => l.EmployeeId == req.Id));
        db.LeaveEntitlements.RemoveRange(db.LeaveEntitlements.Where(l => l.EmployeeId == req.Id));
        db.Notifications    .RemoveRange(db.Notifications    .Where(n => n.EmployeeId == req.Id));
        db.PushSubscriptions.RemoveRange(db.PushSubscriptions.Where(p => p.EmployeeId == req.Id));
        db.AnnouncementReads.RemoveRange(db.AnnouncementReads.Where(a => a.EmployeeId == req.Id));

        // StaffRoles are cascade-deleted automatically via EF config
        db.Staff.Remove(staff);
        await db.SaveChangesAsync(ct);
    }
}
