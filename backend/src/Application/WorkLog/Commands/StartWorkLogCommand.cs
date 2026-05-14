using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Application.WorkLog.Queries;

namespace NightclubWorkforce.Application.WorkLog.Commands;

public record StartWorkLogCommand(Guid EmployeeId, string RoleName, string LocationId, string StartTime, string? Notes) : IRequest<Guid>;

public class StartWorkLogCommandHandler(IAppDbContext db, ISender sender, INotificationHub hub)
    : IRequestHandler<StartWorkLogCommand, Guid>
{
    public async Task<Guid> Handle(StartWorkLogCommand req, CancellationToken ct)
    {
        // Prevent duplicate active shifts
        var active = await db.WorkLogs.AnyAsync(w => w.EmployeeId == req.EmployeeId && w.EndTime == null, ct);
        if (active) throw new InvalidOperationException("You already have an active shift. Please end it first.");

        var location = await db.Locations.FindAsync([req.LocationId], ct)
            ?? throw new KeyNotFoundException($"Location {req.LocationId} not found.");

        var role = await db.Roles.FindAsync([req.RoleName.ToLowerInvariant()], ct)
            ?? throw new KeyNotFoundException($"Role {req.RoleName} not found.");

        // Verify the employee is actually assigned this role (prevents rate fraud)
        var isAssigned = await db.Staff
            .AnyAsync(s => s.Id == req.EmployeeId && s.RoleEntries.Any(r => r.Role == req.RoleName.ToLowerInvariant()), ct);
        if (!isAssigned)
            throw new InvalidOperationException($"You are not assigned the role '{req.RoleName}'.");

        var log = Domain.Entities.WorkLog.StartLive(req.EmployeeId, req.RoleName, req.LocationId, location.Name, req.StartTime, role.HourlyRate, req.Notes);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync(ct);

        // Broadcast updated live stats to all connected dashboard clients
        var stats = await sender.Send(new GetLiveStatsQuery(), ct);
        await hub.BroadcastLiveStatsAsync(stats, ct);

        return log.Id;
    }
}
