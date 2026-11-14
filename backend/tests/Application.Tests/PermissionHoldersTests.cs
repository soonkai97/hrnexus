using HRNexus.Application.Permissions;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// WHY these tests matter: notifications ("a leave request is waiting", "someone asked for a password
/// reset") must reach the people who can ACT on them. That is decided by permissions (role grants plus
/// per-user grants and revokes), never by what a role happens to be called. If this list were wrong, a
/// custom role with <c>leave.approve</c> would never hear about pending leave.
/// </summary>
public class PermissionHoldersTests
{
    private const string Key = AppConstants.Permissions.LeaveApprove;

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<E.Staff> AddStaffAsync(TestDbContext db, string staffId, params Guid[] roleIds)
    {
        var s = E.Staff.Create(staffId, "A", "B", $"{staffId.ToLower()}@test.com", "hash", roleIds.ToList());
        db.Staff.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    private static async Task<Guid> RoleWithAsync(TestDbContext db, string name, params string[] keys)
    {
        var id = await RoleSeed.EnsureRoleAsync(db, name);
        foreach (var k in keys) db.RolePermissions.Add(E.RolePermission.Create(id, k));
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task A_role_that_grants_the_key_makes_its_members_holders()
    {
        await using var db = CreateDb();
        var approvers = await RoleWithAsync(db, "ROTA LEAD", Key);
        var lead = await AddStaffAsync(db, "L1", approvers);

        Assert.Equal([lead.Id], await PermissionHolders.GetIdsAsync(db, Key, CancellationToken.None));
    }

    [Fact]
    public async Task A_role_with_other_permissions_is_not_a_holder_whatever_it_is_called()
    {
        await using var db = CreateDb();
        var named = await RoleWithAsync(db, "MANAGER", AppConstants.Permissions.StaffView);
        var named2 = await RoleWithAsync(db, "SUPERADMIN");
        await AddStaffAsync(db, "M1", named);
        await AddStaffAsync(db, "M2", named2);

        Assert.Empty(await PermissionHolders.GetIdsAsync(db, Key, CancellationToken.None));
    }

    [Fact]
    public async Task A_per_user_grant_makes_a_holder_even_with_no_role_grant()
    {
        await using var db = CreateDb();
        var plain = await RoleWithAsync(db, "CLERK");
        var s = await AddStaffAsync(db, "C1", plain);
        db.StaffPermissionOverrides.Add(E.StaffPermissionOverride.Create(s.Id, Key, granted: true));
        await db.SaveChangesAsync();

        Assert.Equal([s.Id], await PermissionHolders.GetIdsAsync(db, Key, CancellationToken.None));
    }

    [Fact]
    public async Task A_per_user_revoke_wins_over_the_role_grant()
    {
        await using var db = CreateDb();
        var approvers = await RoleWithAsync(db, "ROTA LEAD", Key);
        var kept = await AddStaffAsync(db, "L1", approvers);
        var revoked = await AddStaffAsync(db, "L2", approvers);
        db.StaffPermissionOverrides.Add(E.StaffPermissionOverride.Create(revoked.Id, Key, granted: false));
        await db.SaveChangesAsync();

        Assert.Equal([kept.Id], await PermissionHolders.GetIdsAsync(db, Key, CancellationToken.None));
    }

    [Fact]
    public async Task A_deactivated_account_is_never_a_holder()
    {
        await using var db = CreateDb();
        var approvers = await RoleWithAsync(db, "ROTA LEAD", Key);
        var active = await AddStaffAsync(db, "L1", approvers);
        var gone = await AddStaffAsync(db, "L2", approvers);
        gone.Deactivate();
        await db.SaveChangesAsync();

        Assert.Equal([active.Id], await PermissionHolders.GetIdsAsync(db, Key, CancellationToken.None));
    }

    [Fact]
    public async Task A_holder_of_a_different_key_is_not_listed()
    {
        await using var db = CreateDb();
        var other = await RoleWithAsync(db, "HR", AppConstants.Permissions.StaffManage);
        await AddStaffAsync(db, "H1", other);

        Assert.Empty(await PermissionHolders.GetIdsAsync(db, Key, CancellationToken.None));
    }

    [Fact]
    public async Task Nobody_holding_the_key_is_an_empty_list_not_an_error()
    {
        await using var db = CreateDb();
        Assert.Empty(await PermissionHolders.GetIdsAsync(db, Key, CancellationToken.None));
    }
}
