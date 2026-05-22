using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;
using System.Globalization;

namespace NightclubWorkforce.Application.WorkLog.Commands;

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

        if (!TimeOnly.TryParseExact(req.StartTime, "HH:mm", null, DateTimeStyles.None, out _))
            throw new ArgumentException("StartTime must be in HH:mm format.");
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, DateTimeStyles.None, out _))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        var hours = OvertimeCalculator.CalcHours(req.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from start time.");
        if (hours > 24)
            throw new ArgumentException("A single shift cannot exceed 24 hours.");

        var location = await db.Locations.FindAsync([req.LocationId], ct)
            ?? throw new KeyNotFoundException($"Location '{req.LocationId}' not found.");

        // Prevent date collision: another log for the same employee on the same date (excluding current)
        var hasCollision = await db.WorkLogs.AnyAsync(
            w => w.EmployeeId == log.EmployeeId
              && w.WorkDate   == req.WorkDate
              && w.Id         != req.Id, ct);
        if (hasCollision)
            throw new InvalidOperationException("Another work log already exists for this employee on that date.");

        // Capture before-state for audit trail
        var before = $"Date={log.WorkDate},Start={log.StartTime},End={log.EndTime},Role={log.RoleName},Location={log.LocationName},Notes={log.Notes ?? ""}";

        log.Update(req.RoleName, req.LocationId, location.Name, req.WorkDate, req.StartTime, req.EndTime, req.Notes);

        // Resolve who made the change (caller or manager)
        var actor = await db.Staff
            .Where(s => s.Id == req.CallerId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        var after = $"Date={req.WorkDate},Start={req.StartTime},End={req.EndTime},Role={req.RoleName.ToUpperInvariant()},Location={location.Name},Notes={req.Notes ?? ""}";
        db.AuditLogs.Add(AuditLog.Create(
            entityType:      "WorkLog",
            entityId:        log.Id.ToString(),
            action:          "Updated",
            performedById:   req.CallerId,
            performedByName: actor?.FullName ?? "Unknown",
            changes:         $"Before: {before} | After: {after}"));

        await db.SaveChangesAsync(ct);
    }
}
