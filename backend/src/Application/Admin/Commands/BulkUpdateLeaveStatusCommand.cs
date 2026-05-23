using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Constants;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Admin.Commands;

/// <summary>
/// Bulk-update the status of a list of leave requests (e.g. approve or reject many at once).
/// Sends employee notifications for each affected record.
/// Returns the number of records actually updated.
/// </summary>
public record BulkUpdateLeaveStatusCommand(
    List<Guid> Ids,
    string     Status,
    string?    RejectionReason = null) : IRequest<int>;

public class BulkUpdateLeaveStatusCommandHandler(
    IAppDbContext db, INotificationHub notifHub,
    IPushNotificationService pushSvc, IAuditService audit)
    : IRequestHandler<BulkUpdateLeaveStatusCommand, int>
{
    private static readonly string[] AllowedStatuses =
        [AppConstants.Leave.Approved, AppConstants.Leave.Rejected];

    public async Task<int> Handle(BulkUpdateLeaveStatusCommand req, CancellationToken ct)
    {
        if (!AllowedStatuses.Contains(req.Status, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Bulk update only supports statuses: {string.Join(", ", AllowedStatuses)}.");

        if (req.Ids.Count == 0) return 0;
        if (req.Ids.Count > 100)
            throw new ArgumentException("Cannot bulk-update more than 100 leave requests at once.");

        var leaves = await db.LeaveRequests
            .Where(l => req.Ids.Contains(l.Id))
            .ToListAsync(ct);

        int count = 0;
        foreach (var leave in leaves)
        {
            var prevStatus = leave.Status;
            // Only transition from pending; already-resolved records are skipped
            if (!prevStatus.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase))
                continue;

            leave.Update(leave.LeaveType, leave.StartDate, leave.EndDate,
                leave.StartTime, leave.EndTime, leave.Hours,
                leave.Notes, req.Status, req.RejectionReason);

            await audit.LogAsync("LeaveRequest", leave.Id.ToString(), "BulkStatusUpdate",
                null, "Admin", $"Status → {req.Status}", ct);

            // Notify the employee
            var approved = req.Status.Equals(AppConstants.Leave.Approved, StringComparison.OrdinalIgnoreCase);
            var notif = Notification.Create(
                leave.EmployeeId,
                approved ? "Leave Approved ✓" : "Leave Rejected",
                $"Your {leave.LeaveType} leave ({leave.StartDate:MMM d} – {leave.EndDate:MMM d}) has been {req.Status.ToLower()}.",
                approved ? AppConstants.NotificationType.LeaveApproved : AppConstants.NotificationType.LeaveRejected,
                leave.Id.ToString());
            db.Notifications.Add(notif);
            await notifHub.SendToUserAsync(leave.EmployeeId,
                new NotificationPayload(notif.Id.ToString(), notif.Title, notif.Message,
                    notif.Type, notif.RelatedId, notif.CreatedAt.ToString("yyyy-MM-dd HH:mm")), ct);
            await pushSvc.SendAsync(leave.EmployeeId,
                notif.Title, notif.Message, "/my-leave", ct);

            count++;
        }

        await db.SaveChangesAsync(ct);
        return count;
    }
}
