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
    DateOnly EndDate,
    string   EndTime,
    string?  Notes
) : IRequest
{
    /// <summary>Back-compat constructor for callers without an explicit end date — infers it the
    /// historical way (next day when end ≤ start). The HTTP endpoint always passes EndDate.</summary>
    public UpdateWorkLogCommand(Guid id, Guid callerId, bool isPrivileged, string roleName, string locationId,
        DateOnly workDate, string startTime, string endTime, string? notes)
        : this(id, callerId, isPrivileged, roleName, locationId, workDate, startTime,
            InferEndDate(workDate, startTime, endTime), endTime, notes) { }

    // Tolerant of unparseable times — returns the work date so the handler's format check throws the
    // proper ArgumentException rather than a FormatException at construction.
    private static DateOnly InferEndDate(DateOnly workDate, string startTime, string endTime) =>
        System.TimeOnly.TryParse(startTime, out var s) && System.TimeOnly.TryParse(endTime, out var e)
            ? (e <= s ? workDate.AddDays(1) : workDate)
            : workDate;
}

public class UpdateWorkLogCommandHandler(IAppDbContext db) : IRequestHandler<UpdateWorkLogCommand>
{
    public async Task Handle(UpdateWorkLogCommand req, CancellationToken ct)
    {
        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");

        // Staff cannot edit shift history — only an admin/manager can. Staff must ask an admin to make
        // corrections (keeps the attendance record tamper-resistant).
        if (!req.IsPrivileged)
            throw new UnauthorizedAccessException("Only an admin can edit a shift. Please ask your manager to make the change.");

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

        // Hours from the explicit start/end date+time — a multi-day span is allowed; only a
        // non-positive span or an absurd one (likely a typo) is rejected.
        var hours = ShiftHours.CalcHours(req.WorkDate, req.StartTime, req.EndDate, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("The end date/time must be after the start.");
        if (hours > 14 * 24)
            throw new ArgumentException("That shift spans more than 14 days — please check the dates.");

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
            // Price at the new role's rate as-of the shift's work date, not its current rate.
            newRateSnapshot = await RoleRateLookup.RateAsOfAsync(db, req.RoleName, req.WorkDate, role.HourlyRate, ct);
        }

        // Prevent time overlap with another completed shift for the same employee (excluding this
        // record). Candidates are selected by DATE-interval overlap so a multi-day shift on either
        // side is caught: its end date is at/after our start date AND its start date is at/before our
        // end date. The precise check is then on absolute minutes.
        var nearby = await db.WorkLogs
            .Where(w => w.EmployeeId == log.EmployeeId
                     && w.EndTime != null
                     && w.Id != req.Id
                     && w.EndDate >= req.WorkDate
                     && w.WorkDate <= req.EndDate)
            .Select(w => new { w.WorkDate, w.StartTime, w.EndDate, w.EndTime })
            .ToListAsync(ct);

        var newStart = ShiftHours.AbsMinutes(req.WorkDate, req.StartTime);
        var newEnd   = ShiftHours.AbsMinutes(req.EndDate, req.EndTime);
        foreach (var s in nearby)
        {
            var sEndDate = s.EndDate ?? ShiftHours.DeriveEndDate(s.WorkDate, s.StartTime, s.EndTime!);
            var sStart = ShiftHours.AbsMinutes(s.WorkDate, s.StartTime);
            var sEnd   = ShiftHours.AbsMinutes(sEndDate, s.EndTime!);
            if (newStart < sEnd && sStart < newEnd)
                throw new InvalidOperationException("This shift overlaps with another existing shift.");
        }

        // Capture before-state for audit trail
        var before = $"Date={log.WorkDate},Start={log.StartTime},End={log.EndDate} {log.EndTime},Role={log.RoleName},Rate={log.HourlyRateSnapshot},Location={log.LocationName},Notes={log.Notes ?? ""}";

        log.Update(req.RoleName, req.LocationId, location.Name, req.WorkDate, req.StartTime, req.EndDate, req.EndTime, req.Notes, newRateSnapshot);

        // Resolve who made the change (caller or manager)
        var actor = await db.Staff
            .Where(s => s.Id == req.CallerId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        var after = $"Date={req.WorkDate},Start={req.StartTime},End={req.EndDate} {req.EndTime},Role={req.RoleName.ToUpperInvariant()},Rate={log.HourlyRateSnapshot},Location={location.Name},Notes={req.Notes ?? ""}";
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
}
