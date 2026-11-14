using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Effective-permission logic: union of the user's roles' default permissions, plus per-user
/// granted overrides, minus per-user revoked overrides (revoke wins); deactivated users get
/// nothing; and cache invalidation forces a recompute.
/// </summary>
public class PermissionResolverTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PermissionResolver MakeResolver(TestDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), new PermissionCacheState());

    /// <summary>Seeds an active staff with the given roles + role-default permissions + overrides.</summary>
    private static async Task<Guid> SeedUser(
        TestDbContext db,
        List<string> roles,
        Dictionary<string, string[]>? rolePerms = null,
        (string key, bool granted)[]? overrides = null,
        bool active = true)
    {
        // Ensure a Role row exists for every named role (staff roles + any role-permission keys) and
        // resolve name→id, since staff assignments and role-permissions now key on the role's Guid.
        var roleNames = roles.Concat((rolePerms ?? new()).Keys)
            .Select(n => n.Trim().ToUpperInvariant()).Distinct().ToArray();
        var roleIds = await RoleSeed.EnsureRolesAsync(db, roleNames);

        var staff = E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash",
            roles.Select(r => roleIds[r.Trim().ToUpperInvariant()]).ToList());
        if (!active) staff.Deactivate();
        db.Staff.Add(staff);

        foreach (var (role, keys) in rolePerms ?? new())
            foreach (var key in keys)
                db.RolePermissions.Add(E.RolePermission.Create(roleIds[role.Trim().ToUpperInvariant()], key));

        foreach (var (key, granted) in overrides ?? Array.Empty<(string, bool)>())
            db.StaffPermissionOverrides.Add(E.StaffPermissionOverride.Create(staff.Id, key, granted));

        await db.SaveChangesAsync();
        return staff.Id;
    }

    [Fact]
    public async Task Effective_IsUnionOfRoleDefaults()
    {
        await using var db = CreateDb();
        var id = await SeedUser(db, ["FRONT DESK", "BARTENDER"],
            rolePerms: new() { ["FRONT DESK"] = ["roles.manage", "staff.view"], ["BARTENDER"] = ["reports.view"] });

        var perms = await MakeResolver(db).GetEffectivePermissionsAsync(id);

        Assert.Contains("roles.manage", perms);
        Assert.Contains("staff.view", perms);
        Assert.Contains("reports.view", perms);
    }

    [Fact]
    public async Task Grant_AddsPermissionBeyondRoleDefaults()
    {
        await using var db = CreateDb();
        // EMPLOYEE has no role permissions, but is individually granted role-management access.
        var id = await SeedUser(db, ["EMPLOYEE"], overrides: [("roles.manage", true)]);

        Assert.True(await MakeResolver(db).HasAsync(id, "roles.manage"));
    }

    [Fact]
    public async Task Revoke_WinsOverRoleDefault()
    {
        await using var db = CreateDb();
        var id = await SeedUser(db, ["FRONT DESK"],
            rolePerms: new() { ["FRONT DESK"] = ["roles.manage", "staff.manage"] },
            overrides: [("staff.manage", false)]);   // revoke just this one

        var perms = await MakeResolver(db).GetEffectivePermissionsAsync(id);

        Assert.Contains("roles.manage", perms);          // kept
        Assert.DoesNotContain("staff.manage", perms);    // revoke wins
    }

    [Fact]
    public async Task DeactivatedUser_HasNoPermissions()
    {
        await using var db = CreateDb();
        var id = await SeedUser(db, ["FRONT DESK"],
            rolePerms: new() { ["FRONT DESK"] = ["roles.manage"] }, active: false);

        Assert.Empty(await MakeResolver(db).GetEffectivePermissionsAsync(id));
    }

    [Fact]
    public async Task Invalidate_ForcesRecomputeAfterChange()
    {
        await using var db = CreateDb();
        var id = await SeedUser(db, ["EMPLOYEE"]);
        var resolver = MakeResolver(db);

        Assert.False(await resolver.HasAsync(id, "roles.manage"));   // caches the empty set

        db.StaffPermissionOverrides.Add(E.StaffPermissionOverride.Create(id, "roles.manage", true));
        await db.SaveChangesAsync();

        Assert.False(await resolver.HasAsync(id, "roles.manage"));   // still cached
        resolver.Invalidate(id);
        Assert.True(await resolver.HasAsync(id, "roles.manage"));    // recomputed
    }
}
