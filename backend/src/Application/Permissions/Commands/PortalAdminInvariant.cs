using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.Permissions.Commands;

/// <summary>
/// Protects the invariant: there must always be at least one ACTIVE user who can sign into the admin
/// portal (i.e. effectively holds <c>portal.admin</c>). A permission edit that would drop that count to
/// zero is rejected — otherwise nobody could log in to undo it, an irrecoverable lockout. The guard runs
/// only when an edit actually REMOVES admin access, so it never blocks unrelated edits, and it computes
/// the post-change state in memory (no transaction needed — works on the InMemory test provider too).
/// </summary>
internal static class PortalAdminInvariant
{
    private const string Key = AppConstants.Permissions.PortalAdmin;

    /// <summary>
    /// Counts ACTIVE staff who would still hold effective <c>portal.admin</c> given the set of role names
    /// that grant it (caller passes the POST-change set) plus the current per-user grant/revoke overrides
    /// (revoke wins). Optionally excludes one user whose own change is evaluated separately.
    /// </summary>
    public static async Task<int> CountActiveAdminsAsync(
        IAppDbContext db, List<string> adminRoleNames, Guid? exclude, CancellationToken ct)
    {
        var candidates = new HashSet<Guid>();

        if (adminRoleNames.Count > 0)
        {
            var viaRole = await db.StaffRoles
                .Where(sr => adminRoleNames.Contains(sr.Role))
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

    /// <summary>Whether this user effectively holds <c>portal.admin</c> under the CURRENT committed state.</summary>
    public static async Task<bool> UserHasAdminAsync(IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var active = await db.Staff.Where(s => s.Id == userId).Select(s => s.IsActive).FirstOrDefaultAsync(ct);
        if (!active) return false;

        var ownOverride = await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == userId && o.PermissionKey == Key)
            .Select(o => o.Granted).ToListAsync(ct);
        if (ownOverride.Count > 0) return ownOverride[0];   // a grant/revoke override wins over the role default

        var roles = await db.StaffRoles.Where(sr => sr.EmployeeId == userId).Select(sr => sr.Role).ToListAsync(ct);
        return roles.Count > 0
            && await db.RolePermissions.AnyAsync(rp => roles.Contains(rp.RoleName) && rp.PermissionKey == Key, ct);
    }
}
