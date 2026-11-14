using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// WHY these tests matter: deleting an account is the one way to take someone out that the lock-out
/// guard on deactivate did not cover. If the LAST person who can manage jobs and permissions could be
/// deleted by anyone with <c>staff.manage</c>, nobody could sign in to repair access. And because the
/// protection is by permission (not by an "admin" role name), it must hold for any role that grants
/// <c>roles.manage</c>.
/// </summary>
public class StaffDeleteProtectionTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<E.Staff> AddAsync(TestDbContext db, string staffId, Guid roleId)
    {
        var s = E.Staff.Create(staffId, "A", "B", $"{staffId.ToLower()}@t.com", "hash", [roleId]);
        db.Staff.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    private static async Task<Guid> RoleManagerRoleAsync(TestDbContext db) =>
        await RoleSeed.GrantAsync(db, "ACCESS OWNER", AppConstants.Permissions.RolesManage);

    [Fact]
    public async Task Deleting_the_last_active_holder_of_roles_manage_is_refused()
    {
        await using var db = CreateDb();
        var owner = await AddAsync(db, "OWNER1", await RoleManagerRoleAsync(db));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new DeleteStaffCommandHandler(db).Handle(new DeleteStaffCommand(owner.Id), CancellationToken.None));

        Assert.Contains("last active user", ex.Message);
        Assert.True(await db.Staff.AnyAsync(s => s.Id == owner.Id), "the account must still exist");
    }

    [Fact]
    public async Task Deleting_a_holder_of_roles_manage_is_allowed_while_another_active_holder_remains()
    {
        await using var db = CreateDb();
        var roleId = await RoleManagerRoleAsync(db);
        var first = await AddAsync(db, "OWNER1", roleId);
        await AddAsync(db, "OWNER2", roleId);

        await new DeleteStaffCommandHandler(db).Handle(new DeleteStaffCommand(first.Id), CancellationToken.None);

        Assert.False(await db.Staff.AnyAsync(s => s.Id == first.Id && !s.IsDeleted));
    }

    [Fact]
    public async Task A_deactivated_second_holder_does_not_count_as_a_remaining_one()
    {
        await using var db = CreateDb();
        var roleId = await RoleManagerRoleAsync(db);
        var active = await AddAsync(db, "OWNER1", roleId);
        var inactive = await AddAsync(db, "OWNER2", roleId);
        inactive.Deactivate();
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DeleteStaffCommandHandler(db).Handle(new DeleteStaffCommand(active.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_someone_who_cannot_manage_roles_is_never_blocked()
    {
        await using var db = CreateDb();
        await AddAsync(db, "OWNER1", await RoleManagerRoleAsync(db));
        var clerk = await AddAsync(db, "CLERK1", await RoleSeed.EnsureRoleAsync(db, "CLERK"));

        await new DeleteStaffCommandHandler(db).Handle(new DeleteStaffCommand(clerk.Id), CancellationToken.None);

        Assert.False(await db.Staff.AnyAsync(s => s.Id == clerk.Id && !s.IsDeleted));
    }

    [Fact]
    public async Task The_account_named_HRADMIN_has_no_special_standing_either_way()
    {
        // The seeded first administrator is protected only because it is the last holder of roles.manage,
        // not because of its name: with a second administrator in place it can be deleted.
        await using var db = CreateDb();
        var roleId = await RoleManagerRoleAsync(db);
        var seeded = await AddAsync(db, "HRADMIN", roleId);
        await AddAsync(db, "OWNER2", roleId);

        await new DeleteStaffCommandHandler(db).Handle(new DeleteStaffCommand(seeded.Id), CancellationToken.None);

        Assert.False(await db.Staff.AnyAsync(s => s.Id == seeded.Id && !s.IsDeleted));
    }

    [Fact]
    public async Task Deactivating_the_last_holder_is_still_refused_with_the_same_rule()
    {
        await using var db = CreateDb();
        var owner = await AddAsync(db, "OWNER1", await RoleManagerRoleAsync(db));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DeactivateStaffCommandHandler(db).Handle(new DeactivateStaffCommand(owner.Id), CancellationToken.None));
    }
}
