using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Commands;

public record DeleteRoleCommand(string Name) : IRequest;

public class DeleteRoleCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand req, CancellationToken ct)
    {
        var normalizedName = req.Name.Trim().ToUpperInvariant();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == normalizedName, ct)
            ?? throw new KeyNotFoundException($"Role '{req.Name}' not found.");

        if (role.IsSystem)
            throw new InvalidOperationException($"System role '{req.Name}' cannot be deleted.");

        // Block deletion while the role is still assigned to staff — StaffRole stores the role
        // as a plain string (no FK), so deleting an in-use role would leave dangling references.
        var inUse = await db.StaffRoles.AnyAsync(sr => sr.Role == normalizedName, ct);
        if (inUse)
            throw new InvalidOperationException(
                $"Cannot delete role '{normalizedName}' — it is still assigned to one or more staff. Reassign them first.");

        // Clean up the role's default-permission rows so they don't linger as orphans
        // (RolePermission keys on the role NAME, not an FK, so EF won't cascade them).
        var perms = await db.RolePermissions.Where(rp => rp.RoleName == normalizedName).ToListAsync(ct);
        if (perms.Count > 0) db.RolePermissions.RemoveRange(perms);

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }
}
