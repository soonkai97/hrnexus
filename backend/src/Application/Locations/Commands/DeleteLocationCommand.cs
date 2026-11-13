using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Locations.Commands;

/// <summary>Soft-delete a venue location. Blocked while staff are still assigned to it.</summary>
public record DeleteLocationCommand(string Id) : IRequest;

public class DeleteLocationCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<DeleteLocationCommand>
{
    public async Task Handle(DeleteLocationCommand req, CancellationToken ct)
    {
        var loc = await db.Locations.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Location not found.");

        // Guard: block while staff are still assigned here — otherwise they'd silently lose the
        // assignment. Make the admin unassign them first.
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
