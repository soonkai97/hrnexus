using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Commands;

public record CreateManualWorkLogCommand(Guid EmployeeId, string RoleName, string LocationId, DateOnly WorkDate, string StartTime, string EndTime, string? Notes) : IRequest<Guid>;

public class CreateManualWorkLogCommandHandler(IAppDbContext db) : IRequestHandler<CreateManualWorkLogCommand, Guid>
{
    public async Task<Guid> Handle(CreateManualWorkLogCommand req, CancellationToken ct)
    {
        // Validate HH:mm format
        if (!TimeOnly.TryParseExact(req.StartTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("StartTime must be in HH:mm format.");
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        // Validate time range: supports overnight shifts, but rejects identical or >24h spans
        var hours = OvertimeCalculator.CalcHours(req.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from start time.");
        if (hours > 24)
            throw new ArgumentException("A single shift cannot exceed 24 hours.");

        var location = await db.Locations.FindAsync([req.LocationId], ct)
            ?? throw new KeyNotFoundException($"Location {req.LocationId} not found.");

        var role = await db.Roles.FindAsync([req.RoleName.ToUpperInvariant()], ct)
            ?? throw new KeyNotFoundException($"Role {req.RoleName} not found.");

        // Check for approved leave covering this date
        var workDateUtc = DateTime.SpecifyKind(req.WorkDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var onLeave = await db.LeaveRequests.AnyAsync(
            l => l.EmployeeId == req.EmployeeId
              && l.Status == "approved"
              && l.StartDate <= workDateUtc
              && l.EndDate   >= workDateUtc, ct);
        if (onLeave)
            throw new InvalidOperationException("Cannot start a shift while on approved leave.");

        // Reject if the new shift overlaps in time with any existing completed shift.
        // Check the day before and after to catch overnight shifts that span midnight.
        var prevDate = req.WorkDate.AddDays(-1);
        var nearby = await db.WorkLogs
            .Where(w => w.EmployeeId == req.EmployeeId
                     && w.EndTime != null
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
                throw new InvalidOperationException("This shift overlaps with an existing shift.");
        }

        var log = Domain.Entities.WorkLog.CreateManual(req.EmployeeId, req.RoleName, req.LocationId, location.Name, req.WorkDate, req.StartTime, req.EndTime, role.HourlyRate, req.Notes);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log.Id;
    }

    // Absolute start in minutes from a fixed epoch (DateOnly.DayNumber * 1440 + HH*60 + mm)
    private static long AbsStart(DateOnly date, string time) =>
        (long)date.DayNumber * 1440 + ParseMins(time);

    // Absolute end — adds 1440 if end wraps past midnight (overnight shift)
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
