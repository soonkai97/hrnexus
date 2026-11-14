using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Commands;

/// <summary>
/// Bulk-update the status of a list of leave requests (e.g. approve or reject many at once).
/// Sends employee notifications for each affected record.
/// Returns the number of records actually updated.
/// </summary>
public record BulkUpdateLeaveStatusCommand(
    List<Guid> Ids,
    string     Status,
    string?    RejectionReason = null,
    Guid?      ActingUserId = null) : IRequest<int>;

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

        // Nobody decides their own leave: refuse the whole batch (nothing is changed) rather than
        // silently skipping the approver's own request.
        foreach (var own in leaves.Where(l => l.EmployeeId == req.ActingUserId
                 && l.Status.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase)))
            UpsertLeaveCommandHandler.EnsureNotOwnDecision(req.ActingUserId, own.EmployeeId, req.Status);

        int count = 0;
        // Collect the live notifications to dispatch AFTER the status changes commit. We do not
        // send inside the loop because notifHub/pushSvc are external I/O: a single flaky send
        // would throw and abort the whole batch before SaveChanges ever runs, so none of the
        // approvals would persist. The in-app Notification rows below are saved transactionally,
        // so employees still see the result in the bell even if live delivery fails.
        var toDispatch = new List<(Guid EmployeeId, Notification Notif)>();
        var catalog = await LeaveCatalogReader.GetAsync(db, ct);
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

            // Persist the employee notification (durable, in the same transaction as the status)
            var approved = req.Status.Equals(AppConstants.Leave.Approved, StringComparison.OrdinalIgnoreCase);
            var notif = Notification.Create(
                leave.EmployeeId,
                approved ? "Leave Approved ✓" : "Leave Rejected",
                $"Your {catalog.Phrase(leave.LeaveType)} ({leave.StartDate:MMM d} – {leave.EndDate:MMM d}) has been {req.Status.ToLower()}.",
                approved ? AppConstants.NotificationType.LeaveApproved : AppConstants.NotificationType.LeaveRejected,
                leave.Id.ToString());
            db.Notifications.Add(notif);
            toDispatch.Add((leave.EmployeeId, notif));

            count++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin concurrency token mismatch: the auto-reject Hangfire job (or another admin)
            // modified one of these leave requests between our read and our save.
            // Surface a clean 409-style message so the caller knows to refresh and retry.
            throw new InvalidOperationException(
                "One or more leave requests were modified concurrently. Please refresh and try again.");
        }

        // Best-effort live delivery — only after the batch has durably committed. Each send is
        // isolated so one dead WebSocket connection or push failure can't stop the rest. The
        // persisted Notification rows above guarantee the employee still sees the outcome in-app.
        foreach (var (employeeId, notif) in toDispatch)
        {
            try
            {
                await notifHub.SendToUserAsync(employeeId,
                    new NotificationPayload(notif.Id.ToString(), notif.Title, notif.Message,
                        notif.Type, notif.RelatedId, notif.CreatedAt.ToString("yyyy-MM-dd HH:mm")), ct);
            }
            catch { /* live push is best-effort; in-app notification is already persisted */ }

            try
            {
                await pushSvc.SendAsync(employeeId, notif.Title, notif.Message, "/my-leave", ct);
            }
            catch { /* best-effort */ }
        }

        return count;
    }
}
