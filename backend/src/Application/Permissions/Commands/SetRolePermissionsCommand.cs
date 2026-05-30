using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Permissions.Commands;

/// <summary>Replace a role's default permission set (replace-all semantics).</summary>
public record SetRolePermissionsCommand(string RoleName, List<string> PermissionKeys) : IRequest;

public class SetRolePermissionsHandler(IAppDbContext db, IPermissionResolver resolver)
    : IRequestHandler<SetRolePermissionsCommand>
{
    public async Task Handle(SetRolePermissionsCommand req, CancellationToken ct)
    {
        var name = req.RoleName.Trim().ToUpperInvariant();
        if (!await db.Roles.AnyAsync(r => r.Name == name, ct))
            throw new KeyNotFoundException($"Role '{req.RoleName}' not found.");

        var keys = req.PermissionKeys.Distinct().ToList();
        var unknown = keys.Where(k => !AppConstants.Permissions.All.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown permission(s): {string.Join(", ", unknown)}");

        var existing = await db.RolePermissions.Where(rp => rp.RoleName == name).ToListAsync(ct);
        db.RolePermissions.RemoveRange(existing);
        foreach (var key in keys)
            db.RolePermissions.Add(RolePermission.Create(name, key));

        await db.SaveChangesAsync(ct);
        // A role change can affect many users — invalidate the whole permission cache.
        resolver.InvalidateAll();
    }
}
