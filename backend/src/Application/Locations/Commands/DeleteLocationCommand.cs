using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Locations.Commands;

/// <summary>Soft-delete a venue location. Blocked while a shift is still open (clocked-in) there,
/// so a location someone is actively working at can't vanish mid-shift.</summary>
public record DeleteLocationCommand(string Id) : IRequest;

public class DeleteLocationCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<DeleteLocationCommand>
{
    public async Task Handle(DeleteLocationCommand req, CancellationToken ct)
    {
        var loc = await db.Locations.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Location not found.");

        // Guard: an active (not-yet-clocked-out) shift at this location must be closed first.
        var hasActiveShift = await db.WorkLogs
            .AnyAsync(w => w.LocationId == req.Id && w.EndTime == null, ct);
        if (hasActiveShift)
            throw new InvalidOperationException(
                "Cannot delete a location with a shift still in progress. End the open shift first.");

        // Guard: block while staff are still assigned here — otherwise they'd silently lose it from
        // their log-shift options. Make the admin unassign them first.
        var assignedCount = await db.StaffLocations.CountAsync(sl => sl.LocationId == req.Id, ct);
        if (assignedCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete this location — {assignedCount} staff are still assigned to it. Unassign them first.");

        loc.Delete();
        // null performer → AuditService attributes the entry to the authenticated admin.
        await audit.LogAsync("Location", loc.Id, "Deleted", null, "Admin", loc.Name, ct);
        await db.SaveChangesAsync(ct);
    }
}
