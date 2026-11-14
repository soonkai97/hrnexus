using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Permissions.Commands;

/// <summary>Replace a role's default permission set (replace-all semantics).</summary>
public record SetRolePermissionsCommand(string RoleName, List<string> PermissionKeys) : IRequest;

public class SetRolePermissionsHandler(IAppDbContext db, IPermissionResolver resolver)
    : IRequestHandler<SetRolePermissionsCommand>
{
    public async Task Handle(SetRolePermissionsCommand req, CancellationToken ct)
    {
        var name = req.RoleName.Trim().ToUpperInvariant();
        var role = await db.Roles.Where(r => r.Name == name).Select(r => new { r.Id, r.IsSystem }).FirstOrDefaultAsync(ct);
        if (role is null)
            throw new KeyNotFoundException($"Role '{req.RoleName}' not found.");
        var roleId = role.Id;

        // The first administrator role (IsSystem) always holds every permission: the seed re-grants it each
        // start, so an edit here would only be undone silently. Refuse it instead.
        if (role.IsSystem)
            throw new ArgumentException(
                "This is the first administrator role: it always holds every permission, so its permissions can't be edited.");

        var keys = req.PermissionKeys.Distinct().ToList();
        var unknown = keys.Where(k => !AppConstants.Permissions.All.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown permission(s): {string.Join(", ", unknown)}");

        var existing = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);

        // Lockout guard: if this edit strips roles.manage from a role that currently grants it, ensure at
        // least one active user would still have role-management access afterwards (else nobody could log in).
        var roleHadAdmin = existing.Any(rp => rp.PermissionKey == AppConstants.Permissions.RolesManage);
        var roleKeepsAdmin = keys.Contains(AppConstants.Permissions.RolesManage);
        if (roleHadAdmin && !roleKeepsAdmin)
        {
            // Roles still granting roles.manage after this change = current admin roles minus this one.
            var adminRolesAfter = await db.RolePermissions
                .Where(rp => rp.PermissionKey == AppConstants.Permissions.RolesManage && rp.RoleId != roleId)
                .Select(rp => rp.RoleId).Distinct().ToListAsync(ct);
            if (await RolesManageInvariant.CountActiveAdminsAsync(db, adminRolesAfter, exclude: null, ct) == 0)
                throw new ArgumentException(
                    "This change would leave no active user with role-management access. " +
                    "Grant 'Manage roles & permissions' to another role or user first.");
        }

        db.RolePermissions.RemoveRange(existing);
        foreach (var key in keys)
            db.RolePermissions.Add(RolePermission.Create(roleId, key));

        await db.SaveChangesAsync(ct);
        // A role change can affect many users — invalidate the whole permission cache.
        resolver.InvalidateAll();
    }
}
