using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using System.Globalization;

namespace HRNexus.Application.WorkLog.Commands;

public record UpdateWorkLogCommand(
    Guid     Id,
    Guid     CallerId,
    bool     IsPrivileged, // true = Manager/Admin — skip ownership check
    string   RoleName,
    string   LocationId,
    DateOnly WorkDate,
    string   StartTime,
    string   EndTime,
    string?  Notes
) : IRequest;

public class UpdateWorkLogCommandHandler(IAppDbContext db) : IRequestHandler<UpdateWorkLogCommand>
{
    public async Task Handle(UpdateWorkLogCommand req, CancellationToken ct)
    {
        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");

        if (!req.IsPrivileged && log.EmployeeId != req.CallerId)
            throw new UnauthorizedAccessException("Not your shift.");

        if (log.EndTime == null)
            throw new InvalidOperationException("Cannot edit an active shift. End it first.");

        // Block edits that touch a locked payroll period — both the shift's current month and the
        // month it's being moved to (so a locked period can't be escaped by re-dating).
        await PayrollLockGuard.EnsureDateOpenAsync(db, log.WorkDate, ct);
        await PayrollLockGuard.EnsureDateOpenAsync(db, req.WorkDate, ct);

        if (!TimeOnly.TryParseExact(req.StartTime, "HH:mm", null, DateTimeStyles.None, out _))
            throw new ArgumentException("StartTime must be in HH:mm format.");
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, DateTimeStyles.None, out _))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        var hours = ShiftHours.CalcHours(req.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from start time.");
        if (hours > 24)
            throw new ArgumentException("A single shift cannot exceed 24 hours.");

        var location = await db.Locations.FindAsync([req.LocationId], ct)
            ?? throw new KeyNotFoundException($"Location '{req.LocationId}' not found.");

        // The rate is system-derived (never hand-edited). We only touch it when the role actually
        // changes: validate the new role exists and re-snapshot to its CURRENT rate. A time/notes-only
        // edit (role unchanged) leaves the snapshot alone, so an unrelated edit never silently
        // re-prices a historical shift. (A rate that drifted since the work date would need an
        // effective-dated rate history — out of scope here.)
        var roleChanged = log.RoleName != req.RoleName.Trim().ToUpperInvariant();
        decimal? newRateSnapshot = null;
        if (roleChanged)
        {
            var role = await db.Roles.FindAsync([req.RoleName.ToUpperInvariant()], ct)
                ?? throw new KeyNotFoundException($"Role '{req.RoleName}' not found.");
            newRateSnapshot = role.HourlyRate;
        }

        // Prevent time overlap: another completed shift for the same employee whose time range
        // intersects the edited shift (excluding the current record being updated).
        // Check the day before as well to catch overnight shifts from the previous date.
        var prevDate = req.WorkDate.AddDays(-1);
        var nearby = await db.WorkLogs
            .Where(w => w.EmployeeId == log.EmployeeId
                     && w.EndTime != null
                     && w.Id != req.Id
                     && w.WorkDate >= prevDate
                     && w.WorkDate <= req.WorkDate)
            .Select(w => new { w.WorkDate, w.StartTime, w.EndTime })
            .ToListAsync(ct);

        var newStart = AbsStart(req.WorkDate, req.StartTime);
        var newEnd   = AbsEnd(req.WorkDate, req.StartTime, req.EndTime);
        foreach (var s in nearby)
        {
            var sStart = AbsStart(s.WorkDate, s.StartTime);
            var sEnd   = AbsEnd(s.WorkDate, s.StartTime, s.EndTime!);
            if (newStart < sEnd && sStart < newEnd)
                throw new InvalidOperationException("This shift overlaps with another existing shift.");
        }

        // Capture before-state for audit trail
        var before = $"Date={log.WorkDate},Start={log.StartTime},End={log.EndTime},Role={log.RoleName},Rate={log.HourlyRateSnapshot},Location={log.LocationName},Notes={log.Notes ?? ""}";

        log.Update(req.RoleName, req.LocationId, location.Name, req.WorkDate, req.StartTime, req.EndTime, req.Notes, newRateSnapshot);

        // Resolve who made the change (caller or manager)
        var actor = await db.Staff
            .Where(s => s.Id == req.CallerId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        var after = $"Date={req.WorkDate},Start={req.StartTime},End={req.EndTime},Role={req.RoleName.ToUpperInvariant()},Rate={log.HourlyRateSnapshot},Location={location.Name},Notes={req.Notes ?? ""}";
        db.AuditLogs.Add(AuditLog.Create(
            entityType:      "WorkLog",
            entityId:        log.Id.ToString(),
            action:          "Updated",
            performedById:   req.CallerId,
            performedByName: actor?.FullName ?? "Unknown",
            changes:         $"Before: {before} | After: {after}"));

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This shift was just changed by someone else. Please refresh and try again.");
        }
    }

    private static long AbsStart(DateOnly date, string time) =>
        (long)date.DayNumber * 1440 + ParseMins(time);

    private static long AbsEnd(DateOnly date, string startTime, string endTime)
    {
        var s = ParseMins(startTime);
        var e = ParseMins(endTime);
        var dayBase = (long)date.DayNumber * 1440;
        return e > s ? dayBase + e : dayBase + e + 1440;
    }

    private static int ParseMins(string time) =>
        int.Parse(time[..2]) * 60 + int.Parse(time[3..]);
}
