using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Permissions.Commands;

/// <summary>
/// Protects the invariant: there must always be at least one ACTIVE user who can manage jobs and
/// permissions (i.e. effectively holds <c>roles.manage</c>). A permission edit that would drop that count to
/// zero is rejected — otherwise nobody could log in to undo it, an irrecoverable lockout. The guard runs
/// only when an edit actually REMOVES admin access, so it never blocks unrelated edits, and it computes
/// the post-change state in memory (no transaction needed — works on the InMemory test provider too).
/// </summary>
internal static class RolesManageInvariant
{
    private const string Key = AppConstants.Permissions.RolesManage;

    /// <summary>
    /// Counts ACTIVE staff who would still hold effective <c>roles.manage</c> given the set of role ids
    /// that grant it (caller passes the POST-change set) plus the current per-user grant/revoke overrides
    /// (revoke wins). Optionally excludes one user whose own change is evaluated separately.
    /// </summary>
    public static async Task<int> CountActiveAdminsAsync(
        IAppDbContext db, List<Guid> adminRoleIds, Guid? exclude, CancellationToken ct)
    {
        var candidates = new HashSet<Guid>();

        if (adminRoleIds.Count > 0)
        {
            var viaRole = await db.StaffRoles
                .Where(sr => adminRoleIds.Contains(sr.RoleId))
                .Select(sr => sr.EmployeeId)
                .ToListAsync(ct);
            candidates.UnionWith(viaRole);
        }

        var grants = await db.StaffPermissionOverrides
            .Where(o => o.PermissionKey == Key && o.Granted)
            .Select(o => o.EmployeeId).ToListAsync(ct);
        candidates.UnionWith(grants);

        var revokes = await db.StaffPermissionOverrides
            .Where(o => o.PermissionKey == Key && !o.Granted)
            .Select(o => o.EmployeeId).ToListAsync(ct);
        candidates.ExceptWith(revokes);   // revoke wins over role default or grant

        if (exclude is { } ex) candidates.Remove(ex);
        if (candidates.Count == 0) return 0;

        return await db.Staff.Where(s => candidates.Contains(s.Id) && s.IsActive).CountAsync(ct);
    }

    /// <summary>
    /// Lock-out guard for taking a person out of service (deactivate or delete): if they are the last active
    /// user with role-management access, refuse. Protection is by PERMISSION, so it holds for whichever role
    /// grants <c>roles.manage</c>, including the seeded first administrator.
    /// </summary>
    public static async Task EnsureNotTheLastAsync(IAppDbContext db, Guid userId, string action, CancellationToken ct)
    {
        if (!await UserHasAdminAsync(db, userId, ct)) return;

        var adminRoles = await db.RolePermissions
            .Where(rp => rp.PermissionKey == Key)
            .Select(rp => rp.RoleId).Distinct().ToListAsync(ct);
        if (await CountActiveAdminsAsync(db, adminRoles, exclude: userId, ct) == 0)
            throw new ArgumentException(
                $"This is the last active user with role-management access — {action} them would " +
                "lock everyone out. Grant role-management access to another user or role first.");
    }

    /// <summary>Whether this user effectively holds <c>roles.manage</c> under the CURRENT committed state.</summary>
    public static async Task<bool> UserHasAdminAsync(IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var active = await db.Staff.Where(s => s.Id == userId).Select(s => s.IsActive).FirstOrDefaultAsync(ct);
        if (!active) return false;

        var ownOverride = await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == userId && o.PermissionKey == Key)
            .Select(o => o.Granted).ToListAsync(ct);
        if (ownOverride.Count > 0) return ownOverride[0];   // a grant/revoke override wins over the role default

        var roleIds = await db.StaffRoles.Where(sr => sr.EmployeeId == userId).Select(sr => sr.RoleId).ToListAsync(ct);
        return roleIds.Count > 0
            && await db.RolePermissions.AnyAsync(rp => roleIds.Contains(rp.RoleId) && rp.PermissionKey == Key, ct);
    }
}
