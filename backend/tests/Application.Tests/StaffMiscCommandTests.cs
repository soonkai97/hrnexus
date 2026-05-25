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
            "hashed-pass", new List<string> { "BARTENDER" });

    // ── AdminResetPasswordCommand ─────────────────────────────────────────────

    [Fact]
    public async Task AdminResetPassword_ThrowsKeyNotFound_WhenEmployeeDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new AdminResetPasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new AdminResetPasswordCommand(Guid.NewGuid(), "newpass"),
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
        await handler.Handle(new AdminResetPasswordCommand(staff.Id, "newpassword"),
            CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Equal("hashed-newpassword", updated.PasswordHash);
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

        var role = await db.Roles.FindAsync(new object[] { "WAITRESS" });
        Assert.NotNull(role);
        Assert.Equal(15.5m, role.HourlyRate);
    }

    // ── DeleteRoleCommand ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRole_ThrowsKeyNotFound_WhenRoleDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new DeleteRoleCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteRoleCommand("GHOST"), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRole_ThrowsInvalidOperation_WhenSystemRole()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("ADMIN", isSystem: true));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteRoleCommandHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteRoleCommand("ADMIN"), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRole_Succeeds_RemovesRole()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("BARTENDER", isSystem: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteRoleCommandHandler(db);
        await handler.Handle(new DeleteRoleCommand("BARTENDER"), CancellationToken.None);

        var count = await db.Roles.CountAsync();
        Assert.Equal(0, count);
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
        var staff = E.Staff.Create("S002", "Bob", "Lee", "bob@test.com", "hash",
            new List<string> { "MANAGER" },
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
        db.Roles.Add(E.Role.Create("ADMIN", isSystem: true));
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
