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

        loc.Delete();
        // null performer → AuditService attributes the entry to the authenticated admin.
        await audit.LogAsync("Location", loc.Id, "Deleted", null, "Admin", loc.Name, ct);
        await db.SaveChangesAsync(ct);
    }
}
