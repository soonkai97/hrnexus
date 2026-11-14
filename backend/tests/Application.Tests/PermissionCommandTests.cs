using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using HRNexus.Application.Permissions.Queries;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>Role-permission + per-user-override write commands: replace-all, unknown-key rejection,
/// and cache invalidation.</summary>
public class PermissionCommandTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class SpyResolver : IPermissionResolver
    {
        public int InvalidateAllCount;
        public readonly List<Guid> Invalidated = new();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<bool> HasAsync(Guid userId, string key, CancellationToken ct = default) => Task.FromResult(false);
        public void Invalidate(Guid userId) => Invalidated.Add(userId);
        public void InvalidateAll() => InvalidateAllCount++;
    }

    // ── SetRolePermissions ────────────────────────────────────────────────────

    [Fact]
    public async Task SetRolePermissions_ReplacesAll_AndInvalidatesAll()
    {
        await using var db = CreateDb();
        var managerId = await RoleSeed.EnsureRoleAsync(db, "ACCESS OWNER");
        db.RolePermissions.Add(E.RolePermission.Create(managerId, AppConstants.Permissions.PayrollView)); // stale
        await db.SaveChangesAsync();
        var spy = new SpyResolver();

        await new SetRolePermissionsHandler(db, spy).Handle(
            new SetRolePermissionsCommand("access owner",
                [AppConstants.Permissions.RolesManage, AppConstants.Permissions.StaffView]),
            CancellationToken.None);

        var keys = await db.RolePermissions.Where(rp => rp.RoleId == managerId)
            .Select(rp => rp.PermissionKey).ToListAsync();
        Assert.Equal(2, keys.Count);
        Assert.Contains(AppConstants.Permissions.RolesManage, keys);
        Assert.DoesNotContain(AppConstants.Permissions.PayrollView, keys); // replaced, not merged
        Assert.Equal(1, spy.InvalidateAllCount);
    }

    [Fact]
    public async Task SetRolePermissions_ThrowsArgument_OnUnknownKey()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("ACCESS OWNER"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
                new SetRolePermissionsCommand("ACCESS OWNER", ["bogus.key"]), CancellationToken.None));
    }

    [Fact]
    public async Task SetRolePermissions_Refuses_TheFirstAdministratorJob_SoItAlwaysHoldsEveryPermission()
    {
        // The bootstrap job (IsSystem) is how a fresh install gets someone who can set everything up, and the
        // seed re-grants it every new permission. Editing it would only be silently undone at the next start.
        await using var db = CreateDb();
        var bootstrapId = await RoleSeed.EnsureRoleAsync(db, "ADMINISTRATOR", isSystem: true);
        foreach (var key in AppConstants.Permissions.All)
            db.RolePermissions.Add(E.RolePermission.Create(bootstrapId, key));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
                new SetRolePermissionsCommand("ADMINISTRATOR", [AppConstants.Permissions.StaffView]),
                CancellationToken.None));

        Assert.Contains("every permission", ex.Message);
        Assert.Equal(AppConstants.Permissions.All.Count,
            await db.RolePermissions.CountAsync(rp => rp.RoleId == bootstrapId));
    }

    [Fact]
    public async Task SetRolePermissions_ThrowsKeyNotFound_WhenRoleMissing()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
                new SetRolePermissionsCommand("GHOST", [AppConstants.Permissions.RolesManage]),
                CancellationToken.None));
    }

    // ── SetStaffOverrides ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetStaffOverrides_ReplacesAll_AndInvalidatesUser()
    {
        await using var db = CreateDb();
        var staff = E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash", new List<Guid>());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        var spy = new SpyResolver();

        await new SetStaffOverridesHandler(db, spy).Handle(
            new SetStaffOverridesCommand(staff.Id,
            [
                new StaffOverrideDto(AppConstants.Permissions.RolesManage, true),
                new StaffOverrideDto(AppConstants.Permissions.StaffManage, false),
            ]),
            CancellationToken.None);

        var rows = await db.StaffPermissionOverrides.Where(o => o.EmployeeId == staff.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.PermissionKey == AppConstants.Permissions.RolesManage && r.Granted);
        Assert.Contains(rows, r => r.PermissionKey == AppConstants.Permissions.StaffManage && !r.Granted);
        Assert.Contains(staff.Id, spy.Invalidated);
    }

    [Fact]
    public async Task SetStaffOverrides_ThrowsArgument_OnUnknownKey()
    {
        await using var db = CreateDb();
        var staff = E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash", new List<Guid>());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetStaffOverridesHandler(db, new SpyResolver()).Handle(
                new SetStaffOverridesCommand(staff.Id, [new StaffOverrideDto("bogus.key", true)]),
                CancellationToken.None));
    }

    // ── Lockout guard: never leave zero active admin-portal users ──────────────

    [Fact]
    public async Task SetRolePermissions_Throws_WhenStrippingAdminFromTheOnlyAdminRole()
    {
        await using var db = CreateDb();
        var managerId = await RoleSeed.EnsureRoleAsync(db, "ACCESS OWNER");
        db.RolePermissions.Add(E.RolePermission.Create(managerId, AppConstants.Permissions.RolesManage));
        // A live admin who derives portal access solely from this role.
        db.Staff.Add(E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash", new List<Guid> { managerId }));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
                new SetRolePermissionsCommand("ACCESS OWNER", [AppConstants.Permissions.StaffView]),
                CancellationToken.None));
        Assert.Contains("role-management access", ex.Message);

        // Nothing was persisted — the role still grants roles.manage.
        Assert.True(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == managerId && rp.PermissionKey == AppConstants.Permissions.RolesManage));
    }

    [Fact]
    public async Task SetRolePermissions_Allows_StrippingAdmin_WhenAnotherAdminRoleRemains()
    {
        await using var db = CreateDb();
        var roles = await RoleSeed.EnsureRolesAsync(db, "ACCESS OWNER", "SECOND OWNER");
        db.RolePermissions.Add(E.RolePermission.Create(roles["ACCESS OWNER"], AppConstants.Permissions.RolesManage));
        db.RolePermissions.Add(E.RolePermission.Create(roles["SECOND OWNER"], AppConstants.Permissions.RolesManage));
        db.Staff.Add(E.Staff.Create("S002", "Su", "Boss", "su@test.com", "hash", new List<Guid> { roles["SECOND OWNER"] }));
        await db.SaveChangesAsync();

        await new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
            new SetRolePermissionsCommand("ACCESS OWNER", [AppConstants.Permissions.StaffView]),
            CancellationToken.None);

        Assert.False(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == roles["ACCESS OWNER"] && rp.PermissionKey == AppConstants.Permissions.RolesManage));
    }

    [Fact]
    public async Task SetRolePermissions_Allows_StrippingAdmin_WhenAUserGrantOverrideRemains()
    {
        await using var db = CreateDb();
        var managerId = await RoleSeed.EnsureRoleAsync(db, "ACCESS OWNER");
        db.RolePermissions.Add(E.RolePermission.Create(managerId, AppConstants.Permissions.RolesManage));
        // An individual EMPLOYEE granted role-management access directly keeps the system reachable.
        var emp = E.Staff.Create("S003", "Ed", "Lim", "ed@test.com", "hash", new List<Guid>());
        db.Staff.Add(emp);
        db.StaffPermissionOverrides.Add(
            E.StaffPermissionOverride.Create(emp.Id, AppConstants.Permissions.RolesManage, true));
        await db.SaveChangesAsync();

        await new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
            new SetRolePermissionsCommand("ACCESS OWNER", [AppConstants.Permissions.StaffView]),
            CancellationToken.None);

        Assert.False(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == managerId && rp.PermissionKey == AppConstants.Permissions.RolesManage));
    }

    [Fact]
    public async Task SetStaffOverrides_Throws_WhenRevokingAdminFromTheLastAdmin()
    {
        await using var db = CreateDb();
        var managerId = await RoleSeed.EnsureRoleAsync(db, "ACCESS OWNER");
        db.RolePermissions.Add(E.RolePermission.Create(managerId, AppConstants.Permissions.RolesManage));
        var al = E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash", new List<Guid> { managerId });
        db.Staff.Add(al);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetStaffOverridesHandler(db, new SpyResolver()).Handle(
                new SetStaffOverridesCommand(al.Id,
                    [new StaffOverrideDto(AppConstants.Permissions.RolesManage, false)]),
                CancellationToken.None));
        Assert.Contains("last administrator", ex.Message);

        // No revoke row was persisted.
        Assert.False(await db.StaffPermissionOverrides.AnyAsync(o => o.EmployeeId == al.Id));
    }

    [Fact]
    public async Task SetStaffOverrides_Allows_RevokingAdmin_WhenAnotherActiveAdminRemains()
    {
        await using var db = CreateDb();
        var managerId = await RoleSeed.EnsureRoleAsync(db, "ACCESS OWNER");
        db.RolePermissions.Add(E.RolePermission.Create(managerId, AppConstants.Permissions.RolesManage));
        var al = E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash", new List<Guid> { managerId });
        var bo = E.Staff.Create("S002", "Bo", "Tan", "bo@test.com", "hash", new List<Guid> { managerId });
        db.Staff.Add(al);
        db.Staff.Add(bo);
        await db.SaveChangesAsync();

        await new SetStaffOverridesHandler(db, new SpyResolver()).Handle(
            new SetStaffOverridesCommand(al.Id,
                [new StaffOverrideDto(AppConstants.Permissions.RolesManage, false)]),
            CancellationToken.None);

        Assert.True(await db.StaffPermissionOverrides.AnyAsync(o =>
            o.EmployeeId == al.Id && o.PermissionKey == AppConstants.Permissions.RolesManage && !o.Granted));
    }

    [Fact]
    public async Task SetStaffOverrides_Allows_RevokingAdmin_FromAUserWhoNeverHadIt()
    {
        await using var db = CreateDb();
        // No admin anywhere yet — but this EMPLOYEE was never an admin, so revoking is a no-op, not a lockout.
        var emp = E.Staff.Create("S003", "Ed", "Lim", "ed@test.com", "hash", new List<Guid>());
        db.Staff.Add(emp);
        await db.SaveChangesAsync();

        await new SetStaffOverridesHandler(db, new SpyResolver()).Handle(
            new SetStaffOverridesCommand(emp.Id,
                [new StaffOverrideDto(AppConstants.Permissions.RolesManage, false)]),
            CancellationToken.None);

        Assert.True(await db.StaffPermissionOverrides.AnyAsync(o => o.EmployeeId == emp.Id && !o.Granted));
    }
}
