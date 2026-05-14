using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.WorkLog.Commands;

public record CreateManualWorkLogCommand(Guid EmployeeId, string RoleName, string LocationId, DateOnly WorkDate, string StartTime, string EndTime, string? Notes) : IRequest<Guid>;

public class CreateManualWorkLogCommandHandler(IAppDbContext db) : IRequestHandler<CreateManualWorkLogCommand, Guid>
{
    public async Task<Guid> Handle(CreateManualWorkLogCommand req, CancellationToken ct)
    {
        // Validate time range: supports overnight shifts, but rejects identical or >24h spans
        var hours = OvertimeCalculator.CalcHours(req.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from start time.");
        if (hours > 24)
            throw new ArgumentException("A single shift cannot exceed 24 hours.");

        var location = await db.Locations.FindAsync([req.LocationId], ct)
            ?? throw new KeyNotFoundException($"Location {req.LocationId} not found.");

        var role = await db.Roles.FindAsync([req.RoleName.ToLowerInvariant()], ct)
            ?? throw new KeyNotFoundException($"Role {req.RoleName} not found.");

        // Reject if the employee already has any worklog on the same date
        var hasOverlap = await db.WorkLogs
            .AnyAsync(w => w.EmployeeId == req.EmployeeId && w.WorkDate == req.WorkDate, ct);
        if (hasOverlap)
            throw new InvalidOperationException("A work log already exists for this employee on that date.");

        var log = Domain.Entities.WorkLog.CreateManual(req.EmployeeId, req.RoleName, req.LocationId, location.Name, req.WorkDate, req.StartTime, req.EndTime, role.HourlyRate, req.Notes);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log.Id;
    }
}
