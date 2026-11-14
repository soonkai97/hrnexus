using HRNexus.Domain.Constants;
using HRNexus.Infrastructure.Auth;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Phase 7o: no role is special because of its NAME. The first administrator role is found by its flag, it
/// always holds every permission, and the documented first-run account exists only in an empty database.
///
/// WHY these tests matter: the seed runs at every start, and the migration moves real data on existing
/// installs. A wrong seed could lock the first administrator out of a new permission or, worse, bring back
/// the account with the documented default password after someone deleted it. A wrong migration could strip
/// people of access. Only a real Postgres proves the unique-name index, the foreign keys and the SQL.
/// Each test uses its own throwaway database so nothing leaks between them.
/// </summary>
[Collection("postgres")]
public class BootstrapRoleTests(PostgresFixture fx)
{
    private const string PreviousMigration = "20261113161954_RetirePortalAdminPermission";

    private static async Task SeedAsync(AppDbContext db) =>
        await SeedData.SeedDemoUsersAsync(db, new PasswordHasher());

    private async Task<AppDbContext> FreshDbAsync()
    {
        var db = fx.NewDbOn($"boot_{Guid.NewGuid():N}");
        await db.Database.MigrateAsync();
        return db;
    }

    /// <summary>A database as an existing install has it: migrated up to just before this phase.</summary>
    private async Task<AppDbContext> DbBeforeThisPhaseAsync()
    {
        var db = fx.NewDbOn($"boot_{Guid.NewGuid():N}");
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        return db;
    }

    // ── Fresh install ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_fresh_install_gets_one_administrator_job_holding_every_permission_and_HRADMIN()
    {
        await using var db = await FreshDbAsync();
        await SeedAsync(db);

        var roles = await db.Roles.ToListAsync();
        var admin = Assert.Single(roles);
        Assert.Equal("ADMINISTRATOR", admin.Name);
        Assert.True(admin.IsSystem);

        var held = await db.RolePermissions.Where(rp => rp.RoleId == admin.Id).Select(rp => rp.PermissionKey).ToListAsync();
        Assert.Equal(AppConstants.Permissions.All.OrderBy(k => k), held.OrderBy(k => k));

        var hradmin = await db.Staff.SingleAsync(s => s.StaffId == "HRADMIN");
        Assert.Contains(await db.StaffRoles.Where(sr => sr.EmployeeId == hradmin.Id).Select(sr => sr.RoleId).ToListAsync(), id => id == admin.Id);
        Assert.DoesNotContain(roles, r => r.Name is "SUPERADMIN" or "MANAGER");
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var db = await FreshDbAsync();
        await SeedAsync(db);
        await SeedAsync(db);

        Assert.Equal(1, await db.Roles.CountAsync());
        Assert.Equal(AppConstants.Permissions.All.Count, await db.RolePermissions.CountAsync());
        Assert.Equal(1, await db.Staff.CountAsync());
    }

    [Fact]
    public async Task A_renamed_administrator_job_is_still_found_so_no_second_one_is_created()
    {
        await using var db = await FreshDbAsync();
        await SeedAsync(db);
        (await db.Roles.SingleAsync()).SetName("Chief of Staff");
        await db.SaveChangesAsync();

        await SeedAsync(db);

        var role = Assert.Single(await db.Roles.ToListAsync());
        Assert.Equal("CHIEF OF STAFF", role.Name);
        Assert.True(role.IsSystem);
    }

    [Fact]
    public async Task A_permission_added_to_the_catalog_later_is_granted_to_the_administrator_job_at_the_next_start()
    {
        await using var db = await FreshDbAsync();
        await SeedAsync(db);
        var adminId = (await db.Roles.SingleAsync()).Id;
        // Simulate an older install that predates one catalog key.
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(rp => rp.PermissionKey == AppConstants.Permissions.PolicyManage));
        await db.SaveChangesAsync();

        await SeedAsync(db);

        Assert.True(await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminId && rp.PermissionKey == AppConstants.Permissions.PolicyManage));
    }

    [Fact]
    public async Task A_custom_job_already_called_ADMINISTRATOR_is_flagged_instead_of_inserted_twice()
    {
        await using var db = await FreshDbAsync();
        db.Roles.Add(E.Role.Create("Administrator"));   // custom, not flagged
        await db.SaveChangesAsync();

        await SeedAsync(db);   // must not hit the unique name index

        var role = Assert.Single(await db.Roles.ToListAsync());
        Assert.True(role.IsSystem);
        Assert.Equal(AppConstants.Permissions.All.Count, await db.RolePermissions.CountAsync(rp => rp.RoleId == role.Id));
    }

    [Fact]
    public async Task The_default_password_account_is_NOT_brought_back_once_the_database_has_any_account()
    {
        await using var db = await FreshDbAsync();
        await SeedAsync(db);
        var adminRole = (await db.Roles.SingleAsync()).Id;
        // A second administrator exists; the original HRADMIN is deleted (soft-delete) afterwards.
        db.Staff.Add(E.Staff.Create("OWNER2", "Second", "Owner", "owner2@t.com", "hash", [adminRole]));
        await db.SaveChangesAsync();
        db.Staff.Remove(await db.Staff.SingleAsync(s => s.StaffId == "HRADMIN"));
        await db.SaveChangesAsync();

        await SeedAsync(db);   // the next start

        Assert.False(await db.Staff.AnyAsync(s => s.StaffId == "HRADMIN"), "the documented first-run account must stay gone");
        Assert.Equal(2, await db.Staff.IgnoreQueryFilters().CountAsync());   // OWNER2 + the deleted HRADMIN row only
    }

    // ── Existing install: the migration ──────────────────────────────────────────

    [Fact]
    public async Task Migration_renames_SUPERADMIN_keeps_members_and_permissions_and_makes_MANAGER_an_ordinary_job()
    {
        await using var db = await DbBeforeThisPhaseAsync();
        var superadmin = E.Role.Create("SUPERADMIN", isSystem: true);
        var manager = E.Role.Create("MANAGER", isSystem: true);
        db.Roles.AddRange(superadmin, manager);
        await db.SaveChangesAsync();
        db.RolePermissions.Add(E.RolePermission.Create(superadmin.Id, AppConstants.Permissions.RolesManage));
        db.RolePermissions.Add(E.RolePermission.Create(manager.Id, AppConstants.Permissions.StaffView));
        var boss = E.Staff.Create("BOSS1", "Bo", "Ss", "boss@t.com", "hash", [superadmin.Id]);
        var lead = E.Staff.Create("LEAD1", "Le", "Ad", "lead@t.com", "hash", [manager.Id]);
        db.Staff.AddRange(boss, lead);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();   // this phase's migration

        var roles = await db.Roles.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(roles, r => r.Name == "SUPERADMIN");
        var admin = Assert.Single(roles, r => r.Name == "ADMINISTRATOR");
        Assert.Equal(superadmin.Id, admin.Id);   // same row: members and permissions come along
        Assert.True(admin.IsSystem);
        Assert.Contains(await db.StaffRoles.Where(sr => sr.EmployeeId == boss.Id).Select(sr => sr.RoleId).ToListAsync(), id => id == admin.Id);
        Assert.True(await db.RolePermissions.AnyAsync(rp => rp.RoleId == admin.Id && rp.PermissionKey == AppConstants.Permissions.RolesManage));

        var mgr = Assert.Single(roles, r => r.Name == "MANAGER");
        Assert.False(mgr.IsSystem);
        Assert.Contains(await db.StaffRoles.Where(sr => sr.EmployeeId == lead.Id).Select(sr => sr.RoleId).ToListAsync(), id => id == mgr.Id);
        Assert.True(await db.RolePermissions.AnyAsync(rp => rp.RoleId == mgr.Id && rp.PermissionKey == AppConstants.Permissions.StaffView));

        // The next start does not create a second administrator role or the default-password account.
        await SeedAsync(db);
        Assert.Equal(1, await db.Roles.CountAsync(r => r.IsSystem));
        Assert.False(await db.Staff.AnyAsync(s => s.StaffId == "HRADMIN"));
    }

    [Fact]
    public async Task Migration_folds_SUPERADMIN_into_an_existing_ADMINISTRATOR_job_without_losing_anyone()
    {
        await using var db = await DbBeforeThisPhaseAsync();
        var superadmin = E.Role.Create("SUPERADMIN", isSystem: true);
        var custom = E.Role.Create("ADMINISTRATOR");   // the name is taken by a custom role
        db.Roles.AddRange(superadmin, custom);
        await db.SaveChangesAsync();
        db.RolePermissions.Add(E.RolePermission.Create(superadmin.Id, AppConstants.Permissions.RolesManage));
        var boss = E.Staff.Create("BOSS1", "Bo", "Ss", "boss@t.com", "hash", [superadmin.Id]);
        db.Staff.Add(boss);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();

        var roles = await db.Roles.AsNoTracking().ToListAsync();
        var admin = Assert.Single(roles);
        Assert.Equal("ADMINISTRATOR", admin.Name);
        Assert.Equal(custom.Id, admin.Id);
        Assert.True(admin.IsSystem);
        Assert.Contains(await db.StaffRoles.Where(sr => sr.EmployeeId == boss.Id).Select(sr => sr.RoleId).ToListAsync(), id => id == admin.Id);
        Assert.True(await db.RolePermissions.AnyAsync(rp => rp.RoleId == admin.Id && rp.PermissionKey == AppConstants.Permissions.RolesManage));
    }

    [Fact]
    public async Task Migration_is_a_no_op_on_a_database_that_has_neither_old_job()
    {
        await using var db = await DbBeforeThisPhaseAsync();
        db.Roles.Add(E.Role.Create("BARTENDER"));
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();

        var role = Assert.Single(await db.Roles.ToListAsync());
        Assert.Equal("BARTENDER", role.Name);
        Assert.False(role.IsSystem);
    }

    [Fact]
    public async Task Migration_Down_restores_the_old_names_even_if_the_administrator_job_was_renamed()
    {
        await using var db = await DbBeforeThisPhaseAsync();
        db.Roles.AddRange(E.Role.Create("SUPERADMIN", isSystem: true), E.Role.Create("MANAGER", isSystem: true));
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        var admin = await db.Roles.SingleAsync(r => r.Name == "ADMINISTRATOR");
        admin.SetName("Chief of Staff");   // renamed after the migration
        await db.SaveChangesAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);   // Down

        db.ChangeTracker.Clear();
        var roles = await db.Roles.AsNoTracking().ToListAsync();
        Assert.Contains(roles, r => r.Name == "SUPERADMIN" && r.IsSystem);
        Assert.Contains(roles, r => r.Name == "MANAGER" && r.IsSystem);
    }
}
