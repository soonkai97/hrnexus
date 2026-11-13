using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Jobs;

public class LeaveJobs(
    IAppDbContext            db,
    IDateTimeProvider        dateTime,
    IPushNotificationService pushSvc,
    ILogger<LeaveJobs>       logger)
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
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Unique constraint race: an extremely rare concurrent Hangfire execution (or a
            // manual trigger while the scheduled run was in progress) inserted the same
            // (EmployeeId, Year) row between our AnyAsync checks and this save.
            // Log a warning and let Hangfire retry — on retry, AnyAsync will find the
            // existing rows and skip them, so the job will complete with created = 0.
            logger.LogWarning(
                "ResetLeaveEntitlements: unique constraint hit for year {Year} — possible concurrent run. Hangfire will retry.", newYear);
            throw;
        }
        logger.LogInformation(
            "Leave entitlement reset: created {Count} records for year {Year}", created, newYear);
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
        int autoRejected = 0;

        foreach (var leave in overdue)
        {
            // ExecuteUpdateAsync is atomic: the WHERE l.Status == "pending" ensures we only
            // flip leaves that are still pending. If an admin approved/rejected between our
            // ToListAsync and now, affected == 0 and we skip — no stale-overwrite, no
            // phantom push. ExecuteUpdateAsync bypasses the EF change-tracker (no xmin token
            // contention), so a concurrent xmin bump cannot cause a DbUpdateConcurrencyException.
            var newNotes = string.IsNullOrWhiteSpace(leave.Notes)
                ? stamp
                : $"{leave.Notes} | {stamp}";

            var affected = await db.LeaveRequests
                .Where(l => l.Id == leave.Id && l.Status == "pending")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.Status, "rejected")
                    .SetProperty(l => l.Notes, newNotes), ct);

            if (affected == 0)
            {
                logger.LogWarning(
                    "AutoRejectOverdueLeave: skipped leave {Id} — concurrently modified by admin.", leave.Id);
                continue;
            }

            autoRejected++;

            try
            {
                await pushSvc.SendAsync(
                    leave.EmployeeId,
                    "Leave Request Auto-Rejected",
                    $"Your {leave.LeaveType} leave " +
                    $"({leave.StartDate:MMM d}–{leave.EndDate:MMM d}) was auto-rejected " +
                    "as it wasn't reviewed before it ended. Contact your manager to correct this.",
                    "/leave", ct);
            }
            catch { /* push is non-critical */ }
        }

        logger.LogInformation(
            "AutoRejectOverdueLeave: auto-rejected {Count}/{Total} leave(s).", autoRejected, overdue.Count);
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
