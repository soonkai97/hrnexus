using HRNexus.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Permissions;

/// <summary>
/// Who effectively holds a permission right now. Used to decide who is told about something that
/// needs a decision ("a leave request is waiting", "someone asked for a password reset"): the people
/// who can act on it, never the people who happen to have a particular role NAME.
/// Same rule as <c>PermissionResolver</c>: a role's default permissions, plus per-user grants, minus
/// per-user revokes (revoke wins). Only active, not-deleted accounts; one set of queries, no cache
/// (a fan-out is rare and must reflect the permissions as they are now).
/// </summary>
public static class PermissionHolders
{
    public static async Task<List<Guid>> GetIdsAsync(IAppDbContext db, string permissionKey, CancellationToken ct)
    {
        var roleIds = await db.RolePermissions
            .Where(rp => rp.PermissionKey == permissionKey)
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(ct);

        var holders = new HashSet<Guid>();
        if (roleIds.Count > 0)
        {
            var viaRole = await db.StaffRoles
                .Where(sr => roleIds.Contains(sr.RoleId))
                .Select(sr => sr.EmployeeId)
                .ToListAsync(ct);
            holders.UnionWith(viaRole);
        }

        var overrides = await db.StaffPermissionOverrides
            .Where(o => o.PermissionKey == permissionKey)
            .Select(o => new { o.EmployeeId, o.Granted })
            .ToListAsync(ct);
        foreach (var o in overrides) { if (o.Granted) holders.Add(o.EmployeeId); }
        foreach (var o in overrides) { if (!o.Granted) holders.Remove(o.EmployeeId); }   // revoke wins

        if (holders.Count == 0) return [];

        // Start from Staff so deactivated and soft-deleted accounts drop out (the soft-delete query
        // filter applies to db.Staff in the real context).
        return await db.Staff
            .Where(s => holders.Contains(s.Id) && s.IsActive && !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }
}
