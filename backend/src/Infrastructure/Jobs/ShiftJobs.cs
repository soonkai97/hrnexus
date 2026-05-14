using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Jobs;

public class ShiftJobs(
    IAppDbContext            db,
    IDateTimeProvider        dateTime,
    IPushNotificationService pushSvc,
    ILogger<ShiftJobs>       logger)
{
    /// <summary>
    /// Reset annual leave entitlements at start of each new year.
    /// Runs on January 1st via Hangfire cron.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    public async Task ResetLeaveEntitlementsAsync()
    {
        var newYear = dateTime.LocalNow.Year;

        var activeStaff = await db.Staff
            .Where(s => s.IsActive)
            .Select(s => s.Id)
            .ToListAsync();

        int created = 0;
        foreach (var staffId in activeStaff)
        {
            var exists = await db.LeaveEntitlements
                .AnyAsync(e => e.EmployeeId == staffId && e.Year == newYear);
            if (!exists)
            {
                db.LeaveEntitlements.Add(LeaveEntitlement.Create(staffId, newYear));
                created++;
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation(
            "Leave entitlement reset: created {Count} records for year {Year}", created, newYear);
    }

    /// <summary>
    /// Send push reminders to staff with active (open) work logs that have been
    /// running for more than 12 hours, as a safety net.
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    public async Task RemindLongRunningWorkLogsAsync()
    {
        var cutoff = dateTime.UtcNow.AddHours(-12);

        var stale = await db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.EndTime == null && w.CreatedAt <= cutoff)
            .ToListAsync();

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

            logger.LogInformation(
                "Sent long-running work log reminder to employee {EmployeeId} for log {WorkLogId}",
                wl.EmployeeId, wl.Id);
        }

        // Notify all active managers and admins so they can follow up.
        if (stale.Count > 0)
        {
            var supervisorIds = await db.Staff
                .Where(s => s.IsActive &&
                            s.RoleEntries.Any(r => r.Role == "manager" || r.Role == "admin"))
                .Select(s => s.Id)
                .ToListAsync();

            if (supervisorIds.Count > 0)
            {
                var body = stale.Count == 1
                    ? $"{stale[0].Employee?.FullName ?? "A staff member"} has been on shift for over 12 hours without clocking out."
                    : $"{stale.Count} staff members have been on shift for 12+ hours without clocking out.";

                try
                {
                    await pushSvc.SendToManyAsync(
                        supervisorIds,
                        "⚠️ Staff haven't clocked out",
                        body,
                        "/");   // admin portal dashboard
                }
                catch { /* non-critical */ }

                logger.LogInformation(
                    "Sent stale-shift alert to {Count} supervisor(s) for {StaleCount} open log(s)",
                    supervisorIds.Count, stale.Count);
            }
        }
    }

    /// <summary>
    /// Auto-reject any pending leave requests whose end date has already passed.
    /// Runs daily at 02:00 UTC so nothing slips through overnight.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    public async Task AutoRejectOverdueLeaveAsync()
    {
        var today = dateTime.UtcNow.Date;

        var overdue = await db.LeaveRequests
            .Include(l => l.Employee)
            .Where(l => l.Status == "pending" && l.EndDate.Date < today)
            .ToListAsync();

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

        await db.SaveChangesAsync();
        logger.LogInformation(
            "AutoRejectOverdueLeave: auto-rejected {Count} leave(s).", overdue.Count);
    }
}
