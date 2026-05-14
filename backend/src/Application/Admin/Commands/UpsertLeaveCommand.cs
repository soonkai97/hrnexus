using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Admin.Commands;

public record UpsertLeaveCommand(
    Guid?    Id,           // null = create, non-null = update
    Guid     EmployeeId,
    string   LeaveType,
    DateTime StartDate,
    DateTime EndDate,
    string?  Notes,
    string   Status) : IRequest<Guid>;

public class UpsertLeaveCommandHandler(IAppDbContext db, INotificationHub notifHub,
    IPushNotificationService pushSvc, IAuditService audit)
    : IRequestHandler<UpsertLeaveCommand, Guid>
{
    public async Task<Guid> Handle(UpsertLeaveCommand req, CancellationToken ct)
    {
        if (req.StartDate > req.EndDate)
            throw new ArgumentException("StartDate must be on or before EndDate.");

        var durationDays = (req.EndDate.Date - req.StartDate.Date).TotalDays + 1;
        if (durationDays > 60)
            throw new ArgumentException("Leave duration cannot exceed 60 days in a single request.");

        if (req.StartDate.Date > DateTime.UtcNow.Date.AddYears(1))
            throw new ArgumentException("Leave cannot be requested more than 1 year in advance.");

        var start = DateTime.SpecifyKind(req.StartDate.Date, DateTimeKind.Utc);
        var end   = DateTime.SpecifyKind(req.EndDate.Date,   DateTimeKind.Utc);

        // Overlap check — block any overlapping leave for same employee.
        var hasOverlap = await db.LeaveRequests
            .AnyAsync(l => l.EmployeeId == req.EmployeeId
                        && l.Id != (req.Id ?? Guid.Empty)
                        && l.Status != "rejected"
                        && l.StartDate <= end
                        && l.EndDate   >= start, ct);

        if (hasOverlap)
            throw new InvalidOperationException(
                "A leave request already exists overlapping the selected date range.");

        if (req.Id.HasValue)
        {
            var existing = await db.LeaveRequests.FindAsync([req.Id.Value], ct)
                ?? throw new KeyNotFoundException($"Leave request {req.Id} not found.");

            var prevStatus = existing.Status;
            existing.Update(req.LeaveType, req.StartDate, req.EndDate,
                req.Notes, req.Status);
            await db.SaveChangesAsync(ct);

            // Notify employee when status transitions to approved / rejected
            if (!prevStatus.Equals(req.Status, StringComparison.OrdinalIgnoreCase)
                && (req.Status.Equals("approved", StringComparison.OrdinalIgnoreCase)
                    || req.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase)))
            {
                await CreateAndSendNotification(existing.EmployeeId, existing.Id,
                    req.Status.ToLower(), req.LeaveType, req.StartDate, req.EndDate, ct);
            }

            await audit.LogAsync("LeaveRequest", existing.Id.ToString(), "Updated",
                null, "Admin", $"Status → {req.Status}", ct);

            return existing.Id;
        }
        else
        {
            var leave = LeaveRequest.Create(req.EmployeeId, req.LeaveType,
                req.StartDate, req.EndDate, req.Notes);
            // Apply status immediately if not default
            if (!req.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
                leave.Update(req.LeaveType, req.StartDate, req.EndDate,
                    req.Notes, req.Status);
            db.LeaveRequests.Add(leave);
            await db.SaveChangesAsync(ct);

            await audit.LogAsync("LeaveRequest", leave.Id.ToString(), "Created",
                req.EmployeeId, "Staff", $"{req.LeaveType} {req.StartDate:yyyy-MM-dd}–{req.EndDate:yyyy-MM-dd}", ct);

            // Notify managers of new pending leave
            if (req.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
            {
                var managers = await db.Staff
                    .Where(e => e.IsActive &&
                        (e.RoleEntries.Any(r => r.Role == "manager") ||
                         e.RoleEntries.Any(r => r.Role == "admin")))
                    .ToListAsync(ct);

                var emp = await db.Staff.FindAsync([req.EmployeeId], ct);
                foreach (var mgr in managers)
                {
                    var notif = Notification.Create(mgr.Id,
                        "New Leave Request",
                        $"{emp?.FullName ?? "An employee"} applied for {req.LeaveType} leave " +
                        $"({req.StartDate:MMM d} – {req.EndDate:MMM d}).",
                        "leave_pending", leave.Id.ToString());
                    db.Notifications.Add(notif);
                    await notifHub.SendToUserAsync(mgr.Id, ToPayload(notif), ct);
                }
                await db.SaveChangesAsync(ct);
            }

            return leave.Id;
        }
    }

    private async Task CreateAndSendNotification(Guid employeeId, Guid leaveId,
        string status, string leaveType, DateTime start, DateTime end, CancellationToken ct)
    {
        var approved = status == "approved";
        var notif = Notification.Create(
            employeeId,
            approved ? "Leave Approved ✓" : "Leave Rejected",
            $"Your {leaveType} leave ({start:MMM d} – {end:MMM d}) has been {status}.",
            approved ? "leave_approved" : "leave_rejected",
            leaveId.ToString());

        db.Notifications.Add(notif);
        await db.SaveChangesAsync(ct);
        await notifHub.SendToUserAsync(employeeId, ToPayload(notif), ct);

        // Also send a browser push notification
        await pushSvc.SendAsync(
            employeeId,
            approved ? "Leave Approved ✓" : "Leave Rejected",
            $"Your {leaveType} leave ({start:MMM d} – {end:MMM d}) has been {status}.",
            "/my-leave", ct);
    }

    private static NotificationPayload ToPayload(Notification n) =>
        new(n.Id.ToString(), n.Title, n.Message, n.Type,
            n.RelatedId, n.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
}
