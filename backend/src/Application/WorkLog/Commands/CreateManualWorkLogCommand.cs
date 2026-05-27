using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

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

        // Reject if the employee already has any worklog on the same date
        var hasOverlap = await db.WorkLogs
            .AnyAsync(w => w.EmployeeId == req.EmployeeId && w.WorkDate == req.WorkDate, ct);
        if (hasOverlap)
            throw new InvalidOperationException("A work log already exists for this employee on that date.");

        var log = Domain.Entities.WorkLog.CreateManual(req.EmployeeId, req.RoleName, req.LocationId, location.Name, req.WorkDate, req.StartTime, req.EndTime, role.HourlyRate, req.Notes);
        db.WorkLogs.Add(log);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Unique index race: a concurrent insert landed the same (EmployeeId, WorkDate)
            // between our AnyAsync check and this save.
            throw new InvalidOperationException("A work log already exists for this employee on that date.");
        }
        return log.Id;
    }
}
