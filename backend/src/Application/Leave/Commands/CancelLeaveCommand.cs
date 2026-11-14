using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Leave.Commands;

public record CancelLeaveCommand(Guid LeaveId, Guid EmployeeId) : IRequest;

public class CancelLeaveCommandHandler(IAppDbContext db, IDateTimeProvider dt, INotificationHub notifHub) : IRequestHandler<CancelLeaveCommand>
{
    public async Task Handle(CancelLeaveCommand req, CancellationToken ct)
    {
        var leave = await db.LeaveRequests
            .FirstOrDefaultAsync(l => l.Id == req.LeaveId && l.EmployeeId == req.EmployeeId, ct)
            ?? throw new KeyNotFoundException("Leave request not found.");
        if (leave.Status != "pending" && leave.Status != "approved")
            throw new InvalidOperationException("Only pending or approved leaves can be cancelled.");
        if (leave.EndDate.Date < dt.LocalNow.Date)
            throw new InvalidOperationException("Cannot cancel a leave that has already ended.");

        // Look up employee name for audit
        var employee = await db.Staff
            .Where(s => s.Id == req.EmployeeId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        var wasApproved = leave.Status == "approved";
        var changes = $"Type={leave.LeaveType},Start={leave.StartDate:yyyy-MM-dd},End={leave.EndDate:yyyy-MM-dd},PreviousStatus={leave.Status}";

        db.AuditLogs.Add(AuditLog.Create(
            entityType:      "LeaveRequest",
            entityId:        leave.Id.ToString(),
            action:          wasApproved ? "WithdrawnApproved" : "Cancelled",
            performedById:   req.EmployeeId,
            performedByName: employee?.FullName ?? "Unknown",
            changes:         changes));

        leave.Cancel();

        // Withdrawing approved leave changes the roster, so tell the managers. The notification rows
        // are saved in the SAME save as the cancellation, so a failure can never report an error for a
        // withdrawal that actually went through. Pending requests were never relied on: silent.
        var notes = wasApproved
            ? await AddManagerNotificationsAsync(leave, employee?.FullName ?? "An employee", req.EmployeeId, ct)
            : [];

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // xmin token mismatch: admin updated this leave between our read and our save.
            // Surface a friendly error so the staff member can refresh and retry.
            throw new InvalidOperationException(
                "This leave request was modified concurrently. Please refresh and try again.");
        }

        // Live delivery is best-effort; the persisted rows above already guarantee the bell shows it.
        foreach (var n in notes)
        {
            try
            {
                await notifHub.SendToUserAsync(n.EmployeeId,
                    new NotificationPayload(n.Id.ToString(), n.Title, n.Message, n.Type,
                        n.RelatedId, n.CreatedAt.ToString("yyyy-MM-dd HH:mm")), ct);
            }
            catch { /* best-effort */ }
        }
    }

    private async Task<List<Notification>> AddManagerNotificationsAsync(LeaveRequest leave, string who, Guid withdrawnBy, CancellationToken ct)
    {
        // Everyone who can approve leave, except the person withdrawing and the leave's owner.
        var approvers = (await PermissionHolders.GetIdsAsync(db, AppConstants.Permissions.LeaveApprove, ct))
            .Where(id => id != withdrawnBy && id != leave.EmployeeId)
            .ToList();
        if (approvers.Count == 0) return [];

        var message = $"{who} withdrew approved {leave.LeaveType} leave " +
                      $"({leave.StartDate:dd/MM} – {leave.EndDate:dd/MM}).";
        var notes = approvers
            .Select(id => Notification.Create(id, "Leave Withdrawn", message,
                AppConstants.NotificationType.LeavePending, leave.Id.ToString()))
            .ToList();
        db.Notifications.AddRange(notes);   // saved by the caller, together with the cancellation
        return notes;
    }
}
