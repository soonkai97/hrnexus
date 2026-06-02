using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;
using HRNexus.Domain.Entities;
using System.Globalization;

namespace HRNexus.Application.WorkLog.Commands;

/// <summary>
/// A manager/admin closes an OPEN shift (a staff member who forgot to clock out) by setting the real
/// end time. This fills a gap between the other two commands:
///   • <see cref="EndWorkLogCommand"/> is owner-only (a staff member ending their own shift), and
///   • <see cref="UpdateWorkLogCommand"/> deliberately refuses active shifts ("End it first").
/// Respects payroll-period locks, writes an audit row, and re-broadcasts live dashboard stats.
/// </summary>
public record AdminEndShiftCommand(Guid Id, Guid CallerId, DateOnly EndDate, string EndTime) : IRequest;

public class AdminEndShiftCommandHandler(IAppDbContext db, ISender sender, INotificationHub hub)
    : IRequestHandler<AdminEndShiftCommand>
{
    public async Task Handle(AdminEndShiftCommand req, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, DateTimeStyles.None, out var endTod))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");

        if (log.EndTime != null)
            throw new InvalidOperationException("Shift already ended.");

        // A forgotten clock-out could sit in a month that has since been locked — guard both the
        // shift's own date and the chosen end date (so a locked period can't be written into).
        await PayrollLockGuard.EnsureDateOpenAsync(db, log.WorkDate, ct);
        await PayrollLockGuard.EnsureDateOpenAsync(db, req.EndDate, ct);

        // Validate the full end date+time against the start. The shift now records an explicit end
        // date, so a multi-day span is allowed — we only reject a non-positive span and an absurd one
        // (a likely typo) so a single shift can't be stretched to weeks.
        var startTod  = TimeOnly.Parse(log.StartTime);
        var startMins = (long)log.WorkDate.DayNumber * 1440 + startTod.Hour * 60 + startTod.Minute;
        var endMins   = (long)req.EndDate.DayNumber  * 1440 + endTod.Hour   * 60 + endTod.Minute;
        var spanMins  = endMins - startMins;
        if (spanMins <= 0)
            throw new ArgumentException("The end date/time must be after the shift started.");
        if (spanMins > 14 * 1440)
            throw new ArgumentException("That end date is more than 14 days after the shift started — please check it.");

        // Closing to a multi-day span could now swallow a later completed shift for the same staff —
        // reject if the resolved interval overlaps one (date-interval candidate query, minute-precise).
        var newStart = ShiftHours.AbsMinutes(log.WorkDate, log.StartTime);
        var newEnd   = ShiftHours.AbsMinutes(req.EndDate, req.EndTime);
        var nearby = await db.WorkLogs
            .Where(w => w.EmployeeId == log.EmployeeId && w.EndTime != null && w.Id != log.Id
                     && w.EndDate >= log.WorkDate && w.WorkDate <= req.EndDate)
            .Select(w => new { w.WorkDate, w.StartTime, w.EndDate, w.EndTime })
            .ToListAsync(ct);
        foreach (var s in nearby)
        {
            var sEndDate = s.EndDate ?? ShiftHours.DeriveEndDate(s.WorkDate, s.StartTime, s.EndTime!);
            if (newStart < ShiftHours.AbsMinutes(sEndDate, s.EndTime!) &&
                ShiftHours.AbsMinutes(s.WorkDate, s.StartTime) < newEnd)
                throw new InvalidOperationException("This shift overlaps with another existing shift for this staff member.");
        }

        var before = $"Date={log.WorkDate},Start={log.StartTime},End=(open)";
        log.End(req.EndDate, req.EndTime);

        var actor = await db.Staff
            .Where(s => s.Id == req.CallerId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        db.AuditLogs.Add(AuditLog.Create(
            entityType:      "WorkLog",
            entityId:        log.Id.ToString(),
            action:          "AdminEnded",
            performedById:   req.CallerId,
            performedByName: actor?.FullName ?? "Unknown",
            changes:         $"Before: {before} | After: End={req.EndDate} {req.EndTime} (admin closed a forgotten clock-out)"));

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This shift was just changed by someone else. Please refresh and try again.");
        }

        // Keep every open dashboard in sync (the on-shift count / cost just changed).
        var stats = await sender.Send(new GetLiveStatsQuery(), ct);
        await hub.BroadcastLiveStatsAsync(stats, ct);
    }
}
