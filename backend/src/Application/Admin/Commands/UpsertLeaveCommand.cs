using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Admin.Commands;

public record UpsertLeaveCommand(
    Guid?    Id,              // null = create, non-null = update
    Guid     EmployeeId,
    string   LeaveType,
    DateTime StartDate,
    DateTime EndDate,
    string?  StartTime,       // HH:mm
    string?  EndTime,         // HH:mm
    decimal? Hours,           // total duration in hours
    string?  Notes,
    string   Status,
    string?  RejectionReason = null) : IRequest<Guid>;

public class UpsertLeaveCommandHandler(IAppDbContext db, INotificationHub notifHub,
    IPushNotificationService pushSvc, IAuditService audit, IDateTimeProvider dt)
    : IRequestHandler<UpsertLeaveCommand, Guid>
{
    public async Task<Guid> Handle(UpsertLeaveCommand req, CancellationToken ct)
    {
        var validTypes = new[]
        {
            AppConstants.Leave.Sick, AppConstants.Leave.Annual,
            AppConstants.Leave.Unpaid, AppConstants.Leave.NoShow,
            AppConstants.Leave.Emergency,
        };
        if (!validTypes.Contains(req.LeaveType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid leave type '{req.LeaveType}'.");

        if (req.StartDate > req.EndDate)
            throw new ArgumentException("StartDate must be on or before EndDate.");

        // Annual leave must be applied at least 3 days in advance (new requests only).
        // The guard is intentionally skipped for updates (req.Id.HasValue = true) so managers
        // can backdate or correct existing annual leave records without hitting this constraint.
        // Staff self-submissions always create new records, so the guard always fires for them.
        if (!req.Id.HasValue
            && req.LeaveType.Equals(AppConstants.Leave.Annual, StringComparison.OrdinalIgnoreCase)
            && req.StartDate.Date < dt.LocalNow.Date.AddDays(AppConstants.Leave.AnnualLeaveAdvanceDays))
        {
            throw new ArgumentException(
                $"Annual leave must be applied at least {AppConstants.Leave.AnnualLeaveAdvanceDays} days in advance. " +
                "For urgent or same-day requests, please select Emergency Leave instead.");
        }

        if (req.Hours is null or <= 0)
            throw new ArgumentException("Hours must be a positive value.");

        if (req.Hours > 720)
            throw new ArgumentException("Leave duration cannot exceed 720 hours (30 days) in a single request.");

        if (req.StartDate.Date > dt.LocalNow.Date.AddYears(1))
            throw new ArgumentException("Leave cannot be requested more than 1 year in advance.");

        // Balance enforcement: annual + emergency share the annual pool; sick has its own pool
        var isRejectedOrCancelled = req.Status.Equals(AppConstants.Leave.Rejected,  StringComparison.OrdinalIgnoreCase)
                                 || req.Status.Equals(AppConstants.Leave.Cancelled, StringComparison.OrdinalIgnoreCase);
        var isEmergency         = req.LeaveType.Equals(AppConstants.Leave.Emergency, StringComparison.OrdinalIgnoreCase);
        var isAnnualOrEmergency = req.LeaveType.Equals(AppConstants.Leave.Annual,    StringComparison.OrdinalIgnoreCase) || isEmergency;
        var isSick              = req.LeaveType.Equals(AppConstants.Leave.Sick,      StringComparison.OrdinalIgnoreCase);

        // RepeatableRead transaction: guards the balance-check read → first SaveChanges against TOCTOU.
        // Two concurrent submissions both passing the balance check and both saving would over-allocate leave.
        // The transaction ensures the second concurrent read sees the first one's write before deciding.
        // InMemory (tests) doesn't support transactions — BeginRepeatableReadAsync returns null, guarded below.
        IDbContextTransaction? tx = await db.BeginRepeatableReadAsync(ct);
        try
        {
            if (!isRejectedOrCancelled && (isAnnualOrEmergency || isSick))
            {
                var year = req.StartDate.Year;

                var entitlement = await db.LeaveEntitlements
                    .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.Year == year, ct);

                // Emergency draws from the annual pool
                decimal entitlementHours = isAnnualOrEmergency
                    ? (entitlement?.AnnualHours ?? 0m)
                    : (entitlement?.SickHours   ?? 0m);

                var poolName = isAnnualOrEmergency ? "annual" : "sick";

                if (entitlementHours <= 0)
                    throw new InvalidOperationException(
                        $"No {poolName} leave entitlement has been set for {year}. Please contact your manager.");

                // Count annual + emergency together against the annual pool
                var usedHours = await db.LeaveRequests
                    .Where(l => l.EmployeeId == req.EmployeeId
                             && (isAnnualOrEmergency
                                 ? (l.LeaveType == AppConstants.Leave.Annual || l.LeaveType == AppConstants.Leave.Emergency)
                                 : l.LeaveType == AppConstants.Leave.Sick)
                             && l.StartDate.Year == year
                             && l.Status != AppConstants.Leave.Rejected
                             && l.Status != AppConstants.Leave.Cancelled
                             && l.Id != (req.Id ?? Guid.Empty))
                    .SumAsync(l => l.Hours ?? 0m, ct);

                if (usedHours + req.Hours > entitlementHours)
                {
                    var remaining = Math.Max(0, entitlementHours - usedHours);
                    throw new InvalidOperationException(
                        $"Insufficient {poolName} leave balance. {remaining:0.##}h remaining.");
                }
            }

            var start = DateTime.SpecifyKind(req.StartDate.Date, DateTimeKind.Utc);
            var end   = DateTime.SpecifyKind(req.EndDate.Date,   DateTimeKind.Utc);

            // Overlap check — block any overlapping leave for same employee.
            var hasOverlap = await db.LeaveRequests
                .AnyAsync(l => l.EmployeeId == req.EmployeeId
                            && l.Id != (req.Id ?? Guid.Empty)
                            && l.Status != AppConstants.Leave.Rejected
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
                    req.StartTime, req.EndTime, req.Hours,
                    req.Notes, req.Status, req.RejectionReason);
                await db.SaveChangesAsync(ct);

                // Commit the leave update; notifications run outside the transaction
                if (tx != null) { await tx.CommitAsync(ct); tx.Dispose(); tx = null; }

                // Notify employee when status transitions to approved / rejected
                if (!prevStatus.Equals(req.Status, StringComparison.OrdinalIgnoreCase)
                    && (req.Status.Equals(AppConstants.Leave.Approved, StringComparison.OrdinalIgnoreCase)
                        || req.Status.Equals(AppConstants.Leave.Rejected, StringComparison.OrdinalIgnoreCase)))
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
                // Sick and emergency leave submitted as pending are auto-approved instantly
                var wasPendingAutoApproved = (isSick || isEmergency)
                    && req.Status.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase);
                var resolvedStatus = wasPendingAutoApproved
                    ? AppConstants.Leave.Approved
                    : req.Status;

                var leave = LeaveRequest.Create(req.EmployeeId, req.LeaveType,
                    req.StartDate, req.EndDate,
                    req.StartTime, req.EndTime, req.Hours,
                    req.Notes);
                // Apply status immediately if not default pending
                if (!resolvedStatus.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase))
                    leave.Update(req.LeaveType, req.StartDate, req.EndDate,
                        req.StartTime, req.EndTime, req.Hours,
                        req.Notes, resolvedStatus);
                db.LeaveRequests.Add(leave);
                await db.SaveChangesAsync(ct);

                // Commit the new leave row; audit + notifications run outside the transaction
                if (tx != null) { await tx.CommitAsync(ct); tx.Dispose(); tx = null; }

                await audit.LogAsync("LeaveRequest", leave.Id.ToString(), "Created",
                    req.EmployeeId, "Staff", $"{req.LeaveType} {req.StartDate:yyyy-MM-dd}–{req.EndDate:yyyy-MM-dd} ({req.Hours:0.##}h)", ct);

                // Notify employee when sick/emergency leave is auto-approved
                if (wasPendingAutoApproved)
                {
                    await CreateAndSendNotification(req.EmployeeId, leave.Id,
                        AppConstants.Leave.Approved, req.LeaveType, req.StartDate, req.EndDate, ct);
                }

                // Notify managers when sick leave is self-reported or emergency leave is taken
                if (wasPendingAutoApproved)
                {
                    var managers = await db.Staff
                        .Where(e => e.IsActive &&
                            (e.RoleEntries.Any(r => r.Role == AppConstants.Roles.Manager) ||
                             e.RoleEntries.Any(r => r.Role == AppConstants.Roles.Admin)))
                        .ToListAsync(ct);

                    var emp = await db.Staff.FindAsync([req.EmployeeId], ct);
                    var notifTitle = isSick ? "Sick Leave Reported" : "Emergency Leave Taken";
                    var notifMsg = isSick
                        ? $"{emp?.FullName ?? "An employee"} has reported sick leave ({req.StartDate:dd/MM} – {req.EndDate:dd/MM}, {req.Hours:0.##}h)."
                        : $"{emp?.FullName ?? "An employee"} has taken emergency leave ({req.StartDate:dd/MM} – {req.EndDate:dd/MM}, {req.Hours:0.##}h).";
                    foreach (var mgr in managers)
                    {
                        var notif = Notification.Create(mgr.Id, notifTitle, notifMsg,
                            AppConstants.NotificationType.LeavePending, leave.Id.ToString());
                        db.Notifications.Add(notif);
                        await notifHub.SendToUserAsync(mgr.Id, ToPayload(notif), ct);
                    }
                    await db.SaveChangesAsync(ct);
                }

                // Notify managers of new pending leave (annual, unpaid, no-show)
                if (resolvedStatus.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase))
                {
                    var managers = await db.Staff
                        .Where(e => e.IsActive &&
                            (e.RoleEntries.Any(r => r.Role == AppConstants.Roles.Manager) ||
                             e.RoleEntries.Any(r => r.Role == AppConstants.Roles.Admin)))
                        .ToListAsync(ct);

                    var emp = await db.Staff.FindAsync([req.EmployeeId], ct);
                    foreach (var mgr in managers)
                    {
                        var notif = Notification.Create(mgr.Id,
                            "New Leave Request",
                            $"{emp?.FullName ?? "An employee"} applied for {req.LeaveType} leave " +
                            $"({req.StartDate:dd/MM} – {req.EndDate:dd/MM}, {req.Hours:0.##}h).",
                            AppConstants.NotificationType.LeavePending, leave.Id.ToString());
                        db.Notifications.Add(notif);
                        await notifHub.SendToUserAsync(mgr.Id, ToPayload(notif), ct);
                    }
                    await db.SaveChangesAsync(ct);
                }

                return leave.Id;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin token mismatch: another session updated the same LeaveRequest row
            // between our read and our SaveChanges. Roll back and surface a friendly error.
            if (tx != null)
                try { tx.Rollback(); } catch { /* ignore rollback errors */ }
            throw new InvalidOperationException(
                "This leave request was modified concurrently. Please refresh and try again.");
        }
        catch
        {
            // Roll back the balance-check transaction if it hasn't been committed yet.
            // If the exception occurs after commit (during notifications), tx is already null — no-op.
            if (tx != null)
                try { tx.Rollback(); } catch { /* ignore rollback errors */ }
            throw;
        }
        finally
        {
            tx?.Dispose(); // safety net — no-op if already committed and disposed above
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
            approved ? AppConstants.NotificationType.LeaveApproved : AppConstants.NotificationType.LeaveRejected,
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
