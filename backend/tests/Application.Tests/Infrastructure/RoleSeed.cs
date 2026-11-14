using Microsoft.EntityFrameworkCore;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests.Infrastructure;

/// <summary>
/// Test helpers for the GUID-keyed Role model. Roles are now identified by a generated <c>Id</c>;
/// staff assignments, role-permissions and role-rates all key on that id. These helpers let tests
/// keep expressing intent in terms of role NAMES (readable) while wiring up the id plumbing.
/// </summary>
public static class RoleSeed
{
    /// <summary>Ensures a Role with the given name exists (creating it if absent) and returns its Id.
    /// Names are uppercase-normalised by <see cref="E.Role.Create"/>, matching production.</summary>
    public static async Task<Guid> EnsureRoleAsync(TestDbContext db, string name, bool isSystem = false)
    {
        var normalized = name.Trim().ToUpperInvariant();
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == normalized);
        if (existing != null) return existing.Id;
        var role = E.Role.Create(name, isSystem: isSystem);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>Ensures the role exists and gives it the permission keys (skipping ones it already has);
    /// returns the role id. Access is decided by permissions, so a test that needs "someone who can
    /// approve leave" grants <c>leave.approve</c> to a role instead of relying on a role NAME.</summary>
    public static async Task<Guid> GrantAsync(TestDbContext db, string roleName, params string[] keys)
    {
        var id = await EnsureRoleAsync(db, roleName);
        foreach (var key in keys)
            if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == id && rp.PermissionKey == key))
                db.RolePermissions.Add(E.RolePermission.Create(id, key));
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Ensures each named role exists and returns a name→id map (uppercase keys).</summary>
    public static async Task<Dictionary<string, Guid>> EnsureRolesAsync(TestDbContext db, params string[] names)
    {
        var map = new Dictionary<string, Guid>();
        foreach (var name in names)
            map[name.Trim().ToUpperInvariant()] = await EnsureRoleAsync(db, name);
        return map;
    }

    /// <summary>Resolves a role id by name from the DB (role must already exist).</summary>
    public static async Task<Guid> RoleIdAsync(TestDbContext db, string name)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return await db.Roles.Where(r => r.Name == normalized).Select(r => r.Id).FirstAsync();
    }

    /// <summary>Creates staff assigned to the named roles (seeding the roles first) and adds them to
    /// the context (NOT saved — the caller saves so it can add more in the same unit of work).</summary>
    public static async Task<E.Staff> AddStaffWithRolesAsync(TestDbContext db, string staffId,
        string firstName, string? lastName, string? email, string passwordHash, params string[] roleNames)
    {
        var ids = new List<Guid>();
        foreach (var name in roleNames) ids.Add(await EnsureRoleAsync(db, name));
        var staff = E.Staff.Create(staffId, firstName, lastName, email, passwordHash, ids);
        db.Staff.Add(staff);
        return staff;
    }

    /// <summary>Builds a detached Staff whose RoleEntries have their Role navigation populated — for
    /// pure-domain tests (no DbContext) that read <c>Roles</c> off the nav.</summary>
    public static E.Staff MakeStaffWithRoles(string staffId, string firstName, string? lastName,
        string? email, string passwordHash, params string[] roleNames)
    {
        var roles = roleNames
            .Select(n => E.Role.Create(n))
            .ToList();
        var staff = E.Staff.Create(staffId, firstName, lastName, email, passwordHash,
            roles.Select(r => r.Id).ToList());
        // Attach the Role nav on each entry so Staff.Roles resolves without a DB load.
        foreach (var entry in staff.RoleEntries)
            entry.Role = roles.First(r => r.Id == entry.RoleId);
        return staff;
    }
}
