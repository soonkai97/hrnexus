using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using HRNexus.Application.Permissions.Queries;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using E = HRNexus.Domain.Entities;
using Xunit;

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
        db.Roles.Add(E.Role.Create("MANAGER", isSystem: true));
        db.RolePermissions.Add(E.RolePermission.Create("MANAGER", AppConstants.Permissions.LedgerManage)); // stale
        await db.SaveChangesAsync();
        var spy = new SpyResolver();

        await new SetRolePermissionsHandler(db, spy).Handle(
            new SetRolePermissionsCommand("manager",
                [AppConstants.Permissions.PortalAdmin, AppConstants.Permissions.StaffView]),
            CancellationToken.None);

        var keys = await db.RolePermissions.Where(rp => rp.RoleName == "MANAGER")
            .Select(rp => rp.PermissionKey).ToListAsync();
        Assert.Equal(2, keys.Count);
        Assert.Contains(AppConstants.Permissions.PortalAdmin, keys);
        Assert.DoesNotContain(AppConstants.Permissions.LedgerManage, keys); // replaced, not merged
        Assert.Equal(1, spy.InvalidateAllCount);
    }

    [Fact]
    public async Task SetRolePermissions_ThrowsArgument_OnUnknownKey()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("MANAGER", isSystem: true));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
                new SetRolePermissionsCommand("MANAGER", ["bogus.key"]), CancellationToken.None));
    }

    [Fact]
    public async Task SetRolePermissions_ThrowsKeyNotFound_WhenRoleMissing()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new SetRolePermissionsHandler(db, new SpyResolver()).Handle(
                new SetRolePermissionsCommand("GHOST", [AppConstants.Permissions.PortalAdmin]),
                CancellationToken.None));
    }

    // ── SetStaffOverrides ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetStaffOverrides_ReplacesAll_AndInvalidatesUser()
    {
        await using var db = CreateDb();
        var staff = E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash", ["EMPLOYEE"]);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        var spy = new SpyResolver();

        await new SetStaffOverridesHandler(db, spy).Handle(
            new SetStaffOverridesCommand(staff.Id,
            [
                new StaffOverrideDto(AppConstants.Permissions.PortalAdmin, true),
                new StaffOverrideDto(AppConstants.Permissions.StaffManage, false),
            ]),
            CancellationToken.None);

        var rows = await db.StaffPermissionOverrides.Where(o => o.EmployeeId == staff.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.PermissionKey == AppConstants.Permissions.PortalAdmin && r.Granted);
        Assert.Contains(rows, r => r.PermissionKey == AppConstants.Permissions.StaffManage && !r.Granted);
        Assert.Contains(staff.Id, spy.Invalidated);
    }

    [Fact]
    public async Task SetStaffOverrides_ThrowsArgument_OnUnknownKey()
    {
        await using var db = CreateDb();
        var staff = E.Staff.Create("S001", "Al", "Wong", "al@test.com", "hash", ["EMPLOYEE"]);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetStaffOverridesHandler(db, new SpyResolver()).Handle(
                new SetStaffOverridesCommand(staff.Id, [new StaffOverrideDto("bogus.key", true)]),
                CancellationToken.None));
    }
}
