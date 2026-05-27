using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Jobs;

public class ShiftJobs(
    IAppDbContext            db,
    IDateTimeProvider        dateTime,
    IPushNotificationService pushSvc,
    INotificationHub         notifHub,
    ILogger<ShiftJobs>       logger)
{
    /// <summary>
    /// Reset annual leave entitlements at start of each new year.
    /// Runs on January 1st via Hangfire cron.
    /// [DisableConcurrentExecution] prevents duplicate inserts when Hangfire retries
    /// or multiple servers try to run the same job simultaneously.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ResetLeaveEntitlementsAsync(CancellationToken ct = default)
    {
        var newYear = dateTime.LocalNow.Year;

        var activeStaff = await db.Staff
            .Where(s => s.IsActive)
            .Select(s => s.Id)
            .ToListAsync(ct);

        int created = 0;
        foreach (var staffId in activeStaff)
        {
            var exists = await db.LeaveEntitlements
                .AnyAsync(e => e.EmployeeId == staffId && e.Year == newYear, ct);
            if (!exists)
            {
                db.LeaveEntitlements.Add(LeaveEntitlement.Create(staffId, newYear));
                created++;
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Leave entitlement reset: created {Count} records for year {Year}", created, newYear);
    }

    /// <summary>
    /// Remind staff with shifts running over 12 hours to clock out.
    /// Sends a browser push (best-effort, every run) and one in-app notification
    /// per stale work log (deduped by RelatedId so the bell doesn't spam).
    /// Runs every 15 minutes via Hangfire.
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RemindLongRunningWorkLogsAsync(CancellationToken ct = default)
    {
        var cutoff = dateTime.UtcNow.AddHours(-12);

        var stale = await db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.EndTime == null && w.CreatedAt <= cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        // ── Browser push (best-effort, fires every run) ────────────────────────
        foreach (var wl in stale)
        {
            try
            {
                await pushSvc.SendAsync(
                    wl.EmployeeId,
                    "⏰ Shift still active",
                    $"Your shift at {wl.LocationName} has been running for over 12 hours. Please end it if you're done.",
                    "/log-shift");
            }
            catch { /* non-critical */ }
        }

        // ── In-app notifications (once per stale work log) ────────────────────
        // Check which logs have already had a notification created so we don't
        // add a new bell entry on every 15-minute tick.
        var staleIds = stale.Select(w => w.Id.ToString()).ToList();
        var alreadyNotifiedIds = await db.Notifications
            .Where(n => n.Type == "stale_shift" && staleIds.Contains(n.RelatedId!))
            .Select(n => n.RelatedId!)
            .ToListAsync(ct);

        var newlyStale = stale
            .Where(w => !alreadyNotifiedIds.Contains(w.Id.ToString()))
            .ToList();

        if (newlyStale.Count == 0) return;

        // Create employee notifications
        var employeeNotifs = newlyStale.Select(wl => (
            wl.EmployeeId,
            Notif: Notification.Create(
                wl.EmployeeId,
                "⏰ Shift still active",
                $"Your shift at {wl.LocationName} has been running for over 12 hours. Please end it if you're done.",
                "stale_shift",
                wl.Id.ToString())
        )).ToList();

        foreach (var (_, notif) in employeeNotifs)
            db.Notifications.Add(notif);

        // Create supervisor notifications
        var supervisorIds = await db.Staff
            .Where(s => s.IsActive &&
                        s.RoleEntries.Any(r => r.Role == AppConstants.Roles.Manager || r.Role == AppConstants.Roles.Admin))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var supervisorBody = newlyStale.Count == 1
            ? $"{newlyStale[0].Employee?.FullName ?? "A staff member"} has been on shift for over 12 hours without clocking out."
            : $"{newlyStale.Count} staff members have been on shift for 12+ hours without clocking out.";

        var supervisorNotifs = supervisorIds.Select(supId => (
            supId,
            Notif: Notification.Create(supId,
                "⚠️ Open shift — no clock-out",
                supervisorBody,
                "stale_shift_admin")
        )).ToList();

        foreach (var (_, notif) in supervisorNotifs)
            db.Notifications.Add(notif);

        await db.SaveChangesAsync(ct);

        // ── Real-time push via SignalR (after save so IDs are stable) ─────────
        foreach (var (employeeId, notif) in employeeNotifs)
        {
            try
            {
                await notifHub.SendToUserAsync(employeeId, new NotificationPayload(
                    notif.Id.ToString(), notif.Title, notif.Message,
                    notif.Type, notif.RelatedId, notif.CreatedAt.ToString("O")));
            }
            catch { /* non-critical */ }
        }

        foreach (var (supId, notif) in supervisorNotifs)
        {
            try
            {
                await notifHub.SendToUserAsync(supId, new NotificationPayload(
                    notif.Id.ToString(), notif.Title, notif.Message,
                    notif.Type, notif.RelatedId, notif.CreatedAt.ToString("O")));
            }
            catch { /* non-critical */ }
        }

        try { await pushSvc.SendToManyAsync(supervisorIds, "⚠️ Staff haven't clocked out", supervisorBody, "/"); }
        catch { /* non-critical */ }

        logger.LogInformation(
            "Stale-shift: created in-app notifications for {EmpCount} employee(s), alerted {SupCount} supervisor(s)",
            newlyStale.Count, supervisorIds.Count);
    }

    /// <summary>
    /// Auto-reject any pending leave requests whose end date has already passed.
    /// Runs daily at 02:00 UTC so nothing slips through overnight.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task AutoRejectOverdueLeaveAsync(CancellationToken ct = default)
    {
        var today = dateTime.LocalNow.Date;

        var overdue = await db.LeaveRequests
            .Include(l => l.Employee)
            .Where(l => l.Status == "pending" && l.EndDate.Date < today)
            .ToListAsync(ct);

        if (overdue.Count == 0)
        {
            logger.LogInformation("AutoRejectOverdueLeave: no overdue pending leaves.");
            return;
        }

        var stamp = $"Auto-rejected {today:yyyy-MM-dd}: leave ended without admin review.";

        foreach (var leave in overdue)
        {
            leave.AutoReject(stamp);

            try
            {
                await pushSvc.SendAsync(
                    leave.EmployeeId,
                    "Leave Request Auto-Rejected",
                    $"Your {leave.LeaveType} leave " +
                    $"({leave.StartDate:MMM d}–{leave.EndDate:MMM d}) was auto-rejected " +
                    "as it wasn't reviewed before it ended. Contact your manager to correct this.",
                    "/leave");
            }
            catch { /* push is non-critical */ }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "AutoRejectOverdueLeave: auto-rejected {Count} leave(s).", overdue.Count);
    }

    /// <summary>
    /// Deletes failed Hangfire job records older than 7 days.
    /// Succeeded jobs already auto-expire after 24 h (Hangfire default).
    /// Failed jobs stay forever unless explicitly deleted — this prevents table bloat.
    /// Runs weekly on Sunday at 04:00 UTC.
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task CleanupOldJobsAsync(CancellationToken ct = default)
    {
        // ── Hangfire: delete failed job records older than 7 days ─────────────
        var monitoring  = JobStorage.Current.GetMonitoringApi();
        var client      = new BackgroundJobClient(JobStorage.Current);
        var jobCutoff   = DateTime.UtcNow.AddDays(-7);

        var failed     = monitoring.FailedJobs(0, 1000);
        int jobsDeleted = 0;

        foreach (var (id, data) in failed)
        {
            if (data.FailedAt.HasValue && data.FailedAt.Value < jobCutoff)
            {
                client.Delete(id);
                jobsDeleted++;
            }
        }

        // ── Notifications: delete records older than 90 days ──────────────────
        var notifCutoff  = dateTime.UtcNow.AddDays(-90);
        int notifsDeleted = await db.Notifications
            .Where(n => n.CreatedAt < notifCutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Weekly cleanup: {Jobs} failed Hangfire job(s), {Notifs} notification(s) older than 90 days removed",
            jobsDeleted, notifsDeleted);
    }
}
