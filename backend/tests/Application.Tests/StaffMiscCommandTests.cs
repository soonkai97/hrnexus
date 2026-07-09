using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Staff.Queries;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for miscellaneous Staff commands and queries:
/// AdminResetPassword, CreateRole, DeleteRole, UpdateProfile, UpdateLanguage,
/// GetStaffById, GetRoles.
/// </summary>
public class StaffMiscCommandTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string p) => "hashed-" + p;
        public bool Verify(string p, string hash) => hash == Hash(p);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static E.Staff MakeStaff(string staffId = "S001")
        => E.Staff.Create(staffId, "Alice", "Wong", "alice@test.com",
            "hashed-pass", new List<Guid>());

    // ── AdminResetPasswordCommand ─────────────────────────────────────────────

    [Fact]
    public async Task AdminResetPassword_ThrowsKeyNotFound_WhenEmployeeDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new AdminResetPasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new AdminResetPasswordCommand(Guid.NewGuid(), "Newpass1"),
                CancellationToken.None));
    }

    [Fact]
    public async Task AdminResetPassword_Succeeds_SetsHashAndClearsRefreshToken()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.SetRefreshToken("old-token", DateTime.UtcNow.AddDays(1));
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new AdminResetPasswordCommandHandler(db, new StubHasher());
        await handler.Handle(new AdminResetPasswordCommand(staff.Id, "Newpassword1"),
            CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Equal("hashed-Newpassword1", updated.PasswordHash);
        Assert.Null(updated.RefreshToken);
    }

    // ── CreateRoleCommand ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRole_ThrowsArgument_WhenNameIsEmpty()
    {
        await using var db = CreateDb();
        var handler = new CreateRoleCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateRoleCommand("  "), CancellationToken.None));
    }

    [Fact]
    public async Task CreateRole_ThrowsInvalidOperation_WhenRoleAlreadyExists()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("BARTENDER"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new CreateRoleCommandHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CreateRoleCommand("bartender"), CancellationToken.None));
    }

    [Fact]
    public async Task CreateRole_Succeeds_UppercasesName()
    {
        await using var db = CreateDb();
        var handler = new CreateRoleCommandHandler(db);

        await handler.Handle(new CreateRoleCommand("waitress", HourlyRate: 15.5m),
            CancellationToken.None);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "WAITRESS");
        Assert.NotNull(role);
        Assert.Equal(15.5m, role.HourlyRate);
        Assert.Equal(30, role.MaxShiftDays);   // defaults to the global cap when unspecified
    }

    [Fact]
    public async Task CreateRole_PersistsCustomMaxShiftDays()
    {
        await using var db = CreateDb();
        var handler = new CreateRoleCommandHandler(db);

        await handler.Handle(new CreateRoleCommand("night guard", MaxShiftDays: 7), CancellationToken.None);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "NIGHT GUARD");
        Assert.NotNull(role);
        Assert.Equal(7, role.MaxShiftDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task CreateRole_ThrowsArgument_WhenMaxShiftDaysOutOfRange(int days)
    {
        await using var db = CreateDb();
        var handler = new CreateRoleCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateRoleCommand("courier", MaxShiftDays: days), CancellationToken.None));
    }

    // ── SetStaffHiddenCommand ─────────────────────────────────────────────────

    [Fact]
    public async Task SetStaffHidden_HidesAndUnhides_WithoutTouchingActive()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new SetStaffHiddenCommandHandler(db);
        await handler.Handle(new SetStaffHiddenCommand(staff.Id, true), CancellationToken.None);
        var hidden = await db.Staff.FirstAsync(s => s.Id == staff.Id);
        Assert.True(hidden.IsHidden);
        Assert.True(hidden.IsActive);   // hiding is presentational — the account stays active

        await handler.Handle(new SetStaffHiddenCommand(staff.Id, false), CancellationToken.None);
        var shown = await db.Staff.FirstAsync(s => s.Id == staff.Id);
        Assert.False(shown.IsHidden);
    }

    [Fact]
    public async Task SetStaffHidden_ThrowsKeyNotFound_WhenStaffDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new SetStaffHiddenCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new SetStaffHiddenCommand(Guid.NewGuid(), true), CancellationToken.None));
    }

    // ── DeleteRoleCommand ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRole_ThrowsKeyNotFound_WhenRoleDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new DeleteRoleCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteRoleCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRole_ThrowsInvalidOperation_WhenSystemRole()
    {
        await using var db = CreateDb();
        var role = E.Role.Create("SUPERADMIN", isSystem: true);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteRoleCommandHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRole_Succeeds_RemovesRole()
    {
        await using var db = CreateDb();
        var role = E.Role.Create("BARTENDER", isSystem: false);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteRoleCommandHandler(db);
        await handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None);

        var count = await db.Roles.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteRole_ThrowsInvalidOperation_WhenRoleIsAssignedToStaff()
    {
        // A role still assigned to staff must not be deletable — the StaffRole FK is Restrict,
        // so deleting an in-use role would leave dangling role references.
        await using var db = CreateDb();
        var bartenderId = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        await RoleSeed.AddStaffWithRolesAsync(db, "S001", "Jane", "Doe", "jane@test.com", "hash", "BARTENDER");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteRoleCommandHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteRoleCommand(bartenderId), CancellationToken.None));

        Assert.Equal(1, await db.Roles.CountAsync());   // still there — not deleted
    }

    [Fact]
    public async Task DeleteRole_AlsoRemovesItsRolePermissionRows()
    {
        // Deleting a role removes the role row AND its own default-permission rows, while never touching
        // OTHER roles' rows. DeleteRoleCommand cleans up the deleted role's RolePermission/RoleRate rows
        // EXPLICITLY (by RoleId) — belt-and-suspenders over the Postgres cascade FK — so this holds on the
        // EF InMemory provider too (which does not reproduce DB-level FK cascades).
        await using var db = CreateDb();
        var roles = await RoleSeed.EnsureRolesAsync(db, "BARTENDER", "MANAGER");
        db.RolePermissions.Add(E.RolePermission.Create(roles["BARTENDER"], "staff.view"));
        db.RolePermissions.Add(E.RolePermission.Create(roles["BARTENDER"], "reports.view"));
        db.RolePermissions.Add(E.RolePermission.Create(roles["MANAGER"], "staff.manage"));   // other role — must survive
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteRoleCommandHandler(db);
        await handler.Handle(new DeleteRoleCommand(roles["BARTENDER"]), CancellationToken.None);

        Assert.False(await db.Roles.AnyAsync(r => r.Id == roles["BARTENDER"]));                 // role itself removed
        Assert.False(await db.RolePermissions.AnyAsync(rp => rp.RoleId == roles["BARTENDER"])); // its perms cleaned up
        Assert.True(await db.RolePermissions.AnyAsync(rp => rp.RoleId == roles["MANAGER"]));    // other role untouched
    }

    [Fact]
    public async Task DeleteRole_ThrowsInvalidOperation_WhenShiftInProgressUnderRole()
    {
        // A role with a shift currently in progress under it must not be deletable (mirrors the
        // location active-shift guard). No StaffRole row here, so the assignment guard passes first.
        await using var db = CreateDb();
        var role = E.Role.Create("BARTENDER", isSystem: false);
        db.Roles.Add(role);
        db.WorkLogs.Add(E.WorkLog.StartLive(Guid.NewGuid(), "BARTENDER", "loc-1", "Main Bar",
            new DateOnly(2026, 6, 1), "18:00", 50m));   // EndTime == null → active
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteRoleCommandHandler(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None));

        Assert.Equal(1, await db.Roles.CountAsync());   // still there — not deleted
    }

    // ── UpdateProfileCommand ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfile_ThrowsKeyNotFound_WhenEmployeeDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new UpdateProfileCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new UpdateProfileCommand(Guid.NewGuid(), "+60123456789"),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProfile_Succeeds_UpdatesPhoneNumber()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateProfileCommandHandler(db);
        await handler.Handle(new UpdateProfileCommand(staff.Id, "+60123456789"),
            CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.NotNull(updated.PhoneNumber);
    }

    // ── UpdateLanguageCommand ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateLanguage_ThrowsArgument_WhenUnsupportedLanguage()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateLanguageCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new UpdateLanguageCommand(staff.Id, "fr"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateLanguage_ThrowsKeyNotFound_WhenEmployeeDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new UpdateLanguageCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new UpdateLanguageCommand(Guid.NewGuid(), "en"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateLanguage_Succeeds_WithEnglish()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateLanguageCommandHandler(db);
        await handler.Handle(new UpdateLanguageCommand(staff.Id, "en"), CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Equal("en", updated.PreferredLanguage);
    }

    [Fact]
    public async Task UpdateLanguage_Succeeds_WithChinese()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateLanguageCommandHandler(db);
        await handler.Handle(new UpdateLanguageCommand(staff.Id, "zh"), CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Equal("zh", updated.PreferredLanguage);
    }

    [Fact]
    public async Task UpdateLanguage_Succeeds_WithVietnamese()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateLanguageCommandHandler(db);
        await handler.Handle(new UpdateLanguageCommand(staff.Id, "vi"), CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Equal("vi", updated.PreferredLanguage);
    }

    // ── GetStaffByIdQuery ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetStaffById_ThrowsKeyNotFound_WhenEmployeeDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new GetStaffByIdQueryHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetStaffByIdQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task GetStaffById_ReturnsMappedDto()
    {
        await using var db = CreateDb();
        var managerId = await RoleSeed.EnsureRoleAsync(db, "MANAGER");
        var staff = E.Staff.Create("S002", "Bob", "Lee", "bob@test.com", "hash",
            new List<Guid> { managerId },
            phoneNumber: "+601",
            icNo: "900101-01-1234",
            dateOfBirth: new DateTime(1990, 1, 1));
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffByIdQueryHandler(db);
        var dto = await handler.Handle(new GetStaffByIdQuery(staff.Id), CancellationToken.None);

        Assert.Equal(staff.Id, dto.Id);
        Assert.Equal("S002", dto.StaffId);
        Assert.Equal("Bob Lee", dto.FullName);
        Assert.Contains("MANAGER", dto.Roles);
        Assert.Equal("900101-01-1234", dto.IcNo);
    }

    // ── GetRolesQuery ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRoles_ReturnsEmptyList_WhenNoRoles()
    {
        await using var db = CreateDb();
        var handler = new GetRolesQueryHandler(db);

        var result = await handler.Handle(new GetRolesQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRoles_OrdersNonSystemRolesFirst_ThenSystemRoles()
    {
        // Query: OrderBy(r => r.IsSystem ? 1 : 0) — non-system=0 (first), system=1 (last)
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("BARTENDER", isSystem: false));
        db.Roles.Add(E.Role.Create("SUPERADMIN", isSystem: true));
        db.Roles.Add(E.Role.Create("MANAGER", isSystem: true));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetRolesQueryHandler(db);
        var result = await handler.Handle(new GetRolesQuery(), CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.False(result[0].IsSystem);                       // non-system comes first
        Assert.True(result[1].IsSystem && result[2].IsSystem);  // system roles last
    }

    [Fact]
    public async Task GetRoles_ReturnsMappedDto_WithHourlyRate()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("WAITRESS", isSystem: false, hourlyRate: 25m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetRolesQueryHandler(db);
        var result = await handler.Handle(new GetRolesQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("WAITRESS", result[0].Name);
        Assert.Equal(25m, result[0].HourlyRate);
    }
}
