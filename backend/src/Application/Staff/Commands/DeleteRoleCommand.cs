using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Commands;

public record DeleteRoleCommand(Guid Id) : IRequest;

public class DeleteRoleCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand req, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == req.Id, ct)
            ?? throw new KeyNotFoundException($"Role '{req.Id}' not found.");

        if (role.IsSystem)
            throw new InvalidOperationException($"System role '{role.Name}' cannot be deleted.");

        // Block deletion while the role is still assigned to staff — the StaffRole FK is Restrict,
        // so deleting an in-use role would fail at the DB anyway. Surface a friendly error first.
        var inUse = await db.StaffRoles.AnyAsync(sr => sr.RoleId == req.Id, ct);
        if (inUse)
            throw new InvalidOperationException(
                $"Cannot delete role '{role.Name}' — it is still assigned to one or more staff. Reassign them first.");

        // Block deletion while a shift is currently in progress under this role. WorkLog only keeps a
        // frozen RoleName snapshot (no FK), so the check is on that name — matching role.Name (uppercase).
        var shiftInProgress = await db.WorkLogs.AnyAsync(w => w.RoleName == role.Name && w.EndTime == null, ct);
        if (shiftInProgress)
            throw new InvalidOperationException(
                $"Cannot delete role '{role.Name}' — a shift is currently in progress under this role. End it first.");

        // Explicitly clean up the role's default-permission and rate-history rows. The Postgres FK is
        // ON DELETE CASCADE, but the EF InMemory provider (unit tests) does NOT cascade, so we remove
        // them here to keep behaviour identical on both providers (and avoid orphans on any provider).
        var perms = await db.RolePermissions.Where(rp => rp.RoleId == req.Id).ToListAsync(ct);
        if (perms.Count > 0) db.RolePermissions.RemoveRange(perms);
        var rates = await db.RoleRates.Where(rr => rr.RoleId == req.Id).ToListAsync(ct);
        if (rates.Count > 0) db.RoleRates.RemoveRange(rates);

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }
}
