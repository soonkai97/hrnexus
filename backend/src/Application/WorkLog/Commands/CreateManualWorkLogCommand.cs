using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.WorkLog.Commands;

public record CreateManualWorkLogCommand(Guid EmployeeId, Guid RoleId, string LocationId, DateOnly WorkDate, string StartTime, string EndTime, string? Notes, DateOnly? EndDate = null) : IRequest<Guid>;

public class CreateManualWorkLogCommandHandler(IAppDbContext db) : IRequestHandler<CreateManualWorkLogCommand, Guid>
{
    public async Task<Guid> Handle(CreateManualWorkLogCommand req, CancellationToken ct)
    {
        // Validate HH:mm format
        if (!TimeOnly.TryParseExact(req.StartTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("StartTime must be in HH:mm format.");
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        // The end date is explicit (the client sends it; defaults to the start date), so a manual shift
        // may span multiple days. Fall back to the same-day/overnight inference if none was sent.
        var endDate = req.EndDate ?? ShiftHours.DeriveEndDate(req.WorkDate, req.StartTime, req.EndTime);

        // Duration must be positive and within the sanity cap (multi-day allowed, absurd spans rejected).
        var hours = ShiftHours.CalcHours(req.WorkDate, req.StartTime, endDate, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End must be after start.");
        if (hours > AppConstants.WorkLog.MaxShiftDays * 24)
            throw new ArgumentException($"A shift cannot exceed {AppConstants.WorkLog.MaxShiftDays} days — please check the dates.");

        // Can't add a shift into a locked payroll period.
        await PayrollLockGuard.EnsureDateOpenAsync(db, req.WorkDate, ct);

        var location = await db.Locations.FindAsync([req.LocationId], ct)
            ?? throw new KeyNotFoundException($"Location {req.LocationId} not found.");

        var role = await db.Roles.FindAsync([req.RoleId], ct)
            ?? throw new KeyNotFoundException($"Role {req.RoleId} not found.");

        // Verify the employee is assigned to this location (no assignment ⇒ cannot log a shift here).
        var locationAssigned = await db.Staff
            .AnyAsync(s => s.Id == req.EmployeeId && s.LocationEntries.Any(l => l.LocationId == req.LocationId), ct);
        if (!locationAssigned)
            throw new InvalidOperationException("You are not assigned to this location.");

        // Check for approved leave covering this date
        var workDateUtc = DateTime.SpecifyKind(req.WorkDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var onLeave = await db.LeaveRequests.AnyAsync(
            l => l.EmployeeId == req.EmployeeId
              && l.Status == "approved"
              && l.StartDate <= workDateUtc
              && l.EndDate   >= workDateUtc, ct);
        if (onLeave)
            throw new InvalidOperationException("Cannot start a shift while on approved leave.");

        // Reject if the new shift overlaps in time with any existing completed shift. Candidates are
        // selected by DATE-interval overlap (their end date >= our start date AND their start date <=
        // our end date) so an existing multi-day shift on either side is caught, then compared on
        // absolute minutes.
        var nearby = await db.WorkLogs
            .Where(w => w.EmployeeId == req.EmployeeId
                     && w.EndTime != null
                     && w.EndDate >= req.WorkDate
                     && w.WorkDate <= endDate)
            .Select(w => new { w.WorkDate, w.StartTime, w.EndDate, w.EndTime })
            .ToListAsync(ct);

        var newStart = ShiftHours.AbsMinutes(req.WorkDate, req.StartTime);
        var newEnd   = ShiftHours.AbsMinutes(endDate, req.EndTime);
        foreach (var s in nearby)
        {
            var sEndDate = s.EndDate ?? ShiftHours.DeriveEndDate(s.WorkDate, s.StartTime, s.EndTime!);
            var sStart = ShiftHours.AbsMinutes(s.WorkDate, s.StartTime);
            var sEnd   = ShiftHours.AbsMinutes(sEndDate, s.EndTime!);
            if (newStart < sEnd && sStart < newEnd)
                throw new InvalidOperationException("This shift overlaps with an existing shift.");
        }

        // Price the shift at the rate in effect ON its work date, not the role's current rate —
        // so a back-dated entry made after a raise still uses the historical rate.
        var rateAsOf = await RoleRateLookup.RateAsOfAsync(db, req.RoleId, req.WorkDate, role.HourlyRate, ct);
        var log = Domain.Entities.WorkLog.CreateManual(req.EmployeeId, role.Name, req.LocationId, location.Name, req.WorkDate, req.StartTime, endDate, req.EndTime, rateAsOf, req.Notes, role.PayType);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log.Id;
    }
}
