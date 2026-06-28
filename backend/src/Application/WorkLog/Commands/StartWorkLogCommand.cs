using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;

namespace HRNexus.Application.WorkLog.Commands;

public record StartWorkLogCommand(Guid EmployeeId, string RoleName, string LocationId, string StartTime, string? Notes) : IRequest<Guid>;

public class StartWorkLogCommandHandler(IAppDbContext db, ISender sender, INotificationHub hub, IDateTimeProvider dt)
    : IRequestHandler<StartWorkLogCommand, Guid>
{
    public async Task<Guid> Handle(StartWorkLogCommand req, CancellationToken ct)
    {
        // Validate HH:mm format
        if (!TimeOnly.TryParseExact(req.StartTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("StartTime must be in HH:mm format.");

        // Prevent duplicate active shifts
        var active = await db.WorkLogs.AnyAsync(w => w.EmployeeId == req.EmployeeId && w.EndTime == null, ct);
        if (active) throw new InvalidOperationException("You already have an active shift. Please end it first.");

        var workDate = DateOnly.FromDateTime(dt.LocalNow);

        // The live start time is editable, so guard against clocking in at a time already covered by
        // a completed shift (otherwise a staff member could back-date into a worked period and create
        // an overlap the manual-entry and admin-edit paths already forbid). Candidates are selected by
        // date-interval overlap, then compared on absolute minutes.
        var startAbs = ShiftHours.AbsMinutes(workDate, req.StartTime);
        var doneAround = await db.WorkLogs
            .Where(w => w.EmployeeId == req.EmployeeId && w.EndTime != null
                     && w.EndDate >= workDate && w.WorkDate <= workDate)
            .Select(w => new { w.WorkDate, w.StartTime, w.EndDate, w.EndTime })
            .ToListAsync(ct);
        foreach (var s in doneAround)
        {
            var sEndDate = s.EndDate ?? ShiftHours.DeriveEndDate(s.WorkDate, s.StartTime, s.EndTime!);
            var sStart = ShiftHours.AbsMinutes(s.WorkDate, s.StartTime);
            var sEnd   = ShiftHours.AbsMinutes(sEndDate, s.EndTime!);
            if (startAbs >= sStart && startAbs < sEnd)
                throw new InvalidOperationException("This start time overlaps a shift you already logged.");
        }

        // Check for approved leave covering this date
        var workDateUtc = DateTime.SpecifyKind(workDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var onLeave = await db.LeaveRequests.AnyAsync(
            l => l.EmployeeId == req.EmployeeId
              && l.Status == "approved"
              && l.StartDate <= workDateUtc
              && l.EndDate   >= workDateUtc, ct);
        if (onLeave)
            throw new InvalidOperationException("Cannot start a shift while on approved leave.");

        var location = await db.Locations.FindAsync([req.LocationId], ct)
            ?? throw new KeyNotFoundException($"Location {req.LocationId} not found.");

        var role = await db.Roles.FindAsync([req.RoleName.ToUpperInvariant()], ct)
            ?? throw new KeyNotFoundException($"Role {req.RoleName} not found.");

        // Verify the employee is actually assigned this role (prevents rate fraud)
        var isAssigned = await db.Staff
            .AnyAsync(s => s.Id == req.EmployeeId && s.RoleEntries.Any(r => r.Role == req.RoleName.ToUpperInvariant()), ct);
        if (!isAssigned)
            throw new InvalidOperationException($"You are not assigned the role '{req.RoleName}'.");

        // Verify the employee is assigned to this location (no assignment ⇒ cannot log a shift here).
        var locationAssigned = await db.Staff
            .AnyAsync(s => s.Id == req.EmployeeId && s.LocationEntries.Any(l => l.LocationId == req.LocationId), ct);
        if (!locationAssigned)
            throw new InvalidOperationException("You are not assigned to this location.");

        var log = Domain.Entities.WorkLog.StartLive(req.EmployeeId, req.RoleName, req.LocationId, location.Name, workDate, req.StartTime, role.HourlyRate, req.Notes);
        db.WorkLogs.Add(log);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Partial unique index IX_WorkLogs_EmployeeId_Active fires when a concurrent request
            // slipped past the AnyAsync check above. Surface a clean error instead of a 500.
            throw new InvalidOperationException("You already have an active shift. Please end it first.");
        }

        // Broadcast updated live stats to all connected dashboard clients
        var stats = await sender.Send(new GetLiveStatsQuery(), ct);
        await hub.BroadcastLiveStatsAsync(stats, ct);

        return log.Id;
    }
}
