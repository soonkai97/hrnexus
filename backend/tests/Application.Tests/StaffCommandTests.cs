using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for Staff commands — Register, Update, Delete, Activate, Deactivate.
/// These guard the staff roster from invalid data, duplicate IDs, and unsafe deletions.
/// </summary>
public class StaffCommandTests
{
    // ── Null stubs ────────────────────────────────────────────────────────────

    private sealed class NullAuditService : IAuditService
    {
        public Task LogAsync(string entityType, string entityId, string action,
            Guid? performedById, string performedByName, string? changes = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed-" + password;
        public bool Verify(string password, string hash) => hash == Hash(password);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static E.Staff MakeStaff(
        string staffId   = "S001",
        string firstName = "Alice",
        string lastName  = "Wong",
        string email     = "alice@test.com",
        List<Guid>? roles = null)
        => E.Staff.Create(staffId, firstName, lastName, email, "hash",
            roles ?? new List<Guid>(),
            icNo: "990101-01-1234",
            dateOfBirth: new DateTime(1990, 1, 1));

    private static RegisterStaffCommandHandler RegisterHandler(IAppDbContext db) =>
        new(db, new StubHasher(), new NullAuditService());

    private static UpdateStaffCommandHandler UpdateHandler(IAppDbContext db) =>
        new(db, new NullAuditService());

    private static DeleteStaffCommandHandler DeleteHandler(IAppDbContext db) =>
        new(db);

    private static ActivateStaffCommandHandler ActivateHandler(IAppDbContext db) =>
        new(db);

    private static DeactivateStaffCommandHandler DeactivateHandler(IAppDbContext db) =>
        new(db);

    // ── RegisterStaffCommand ──────────────────────────────────────────────────

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenStaffIdIsEmpty()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "", "Alice", "Wong", "alice@test.com", "Passw0rd1",
                new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenStaffIdExceeds20Chars()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "ABCDEFGHIJKLMNOPQRSTU", // 21 chars
                "Alice", "Wong", "alice@test.com", "Passw0rd1",
                new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenFirstNameIsEmpty()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S001", "", "Wong", "alice@test.com", "Passw0rd1",
                new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsArgument_WhenPasswordTooWeak()
    {
        // Initial password must meet the same policy as a self-service change:
        // 8+ chars, one uppercase, one number. "alllowercase" lacks upper + digit.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S001", "Alice", "Wong", "alice@test.com", "alllowercase",
                new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_Succeeds_WhenLastNameIsEmpty()
    {
        // LastName is now optional — registering with an empty string should succeed.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "", "alice@test.com", "Passw0rd1",
            new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        var saved = await db.Staff.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("", saved.LastName);
    }

    [Fact]
    public async Task Register_Succeeds_WhenEmailIsNull()
    {
        // Email is now optional — staff can be added and receive email later via Edit.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "Wong", null, "Passw0rd1",
            new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        var staff = await db.Staff.FindAsync(id);
        Assert.NotNull(staff);
        Assert.Null(staff.Email);
    }

    [Fact]
    public async Task Register_Succeeds_WhenBothIcAndPassportAreNull()
    {
        // IC and Passport are now optional — staff can be added without identity docs.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "Wong", "alice@test.com", "Passw0rd1",
            new List<Guid>(), IcNo: null, PassportNo: null),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Register_Succeeds_WhenDateOfBirthIsNull()
    {
        // Date of birth is now optional.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "Wong", "alice@test.com", "Passw0rd1",
            new List<Guid>(), DateOfBirth: null),
            CancellationToken.None);

        var staff = await db.Staff.FindAsync(id);
        Assert.Null(staff!.DateOfBirth);
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenStaffIdAlreadyExists()
    {
        // Duplicate staff IDs would cause lookup ambiguity across the whole system.
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001"));
        await db.SaveChangesAsync();

        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "s001", // lowercase — handler normalises to S001 before checking
                "Bob", "Tan", "bob@test.com", "Passw0rd1",
                new List<Guid>(), IcNo: "990202-02-2345", DateOfBirth: new DateTime(1992, 2, 2)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenEmailAlreadyRegistered()
    {
        // Duplicate emails break password-reset flows and unique identification.
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", email: "alice@test.com"));
        await db.SaveChangesAsync();

        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S002", "Bob", "Tan", "Alice@Test.Com", // mixed case — normalised to lowercase
                "Passw0rd1", new List<Guid>(), IcNo: "990202-02-2345", DateOfBirth: new DateTime(1992, 2, 2)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_Succeeds_AndReturnsNewId()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "Wong", "alice@test.com", "Passw0rd1",
            new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Register_SetsStaffId_Uppercased()
    {
        // Staff IDs are normalised to uppercase at rest to prevent case-sensitive duplicates.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "s001", "Alice", "Wong", "alice@test.com", "Passw0rd1",
            new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        var staff = await db.Staff.FindAsync(id);
        Assert.Equal("S001", staff!.StaffId);
    }

    [Fact]
    public async Task Register_SetsEmail_Lowercased()
    {
        // Emails are stored lowercase to ensure case-insensitive uniqueness checks work.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "Wong", "Alice@Test.COM", "Passw0rd1",
            new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        var staff = await db.Staff.FindAsync(id);
        Assert.Equal("alice@test.com", staff!.Email);
    }

    [Fact]
    public async Task Register_HashesPassword_UsingHasher()
    {
        // Passwords must never be stored in plain text.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "Wong", "alice@test.com", "MySecret1",
            new List<Guid>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        var staff = await db.Staff.FindAsync(id);
        // StubHasher produces "hashed-<password>"
        Assert.Equal("hashed-MySecret1", staff!.PasswordHash);
    }

    // ── UpdateStaffCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ThrowsArgument_WhenFirstNameIsEmpty()
    {
        await using var db = CreateDb();
        var existing = MakeStaff();
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new UpdateStaffCommand(
                existing.Id, "S001", "", "Wong", "alice@test.com",
                new List<Guid>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_Succeeds_WhenLastNameIsEmpty()
    {
        // LastName is now optional — clearing it to empty string should succeed.
        await using var db = CreateDb();
        var existing = MakeStaff();
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        await handler.Handle(new UpdateStaffCommand(
            existing.Id, "S001", "Alice", "", "alice@test.com",
            new List<Guid>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        await db.Entry(existing).ReloadAsync();
        Assert.Equal("", existing.LastName);
    }

    [Fact]
    public async Task Update_ThrowsKeyNotFound_WhenStaffDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = UpdateHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new UpdateStaffCommand(
                Guid.NewGuid(), "S001", "Alice", "Wong", "alice@test.com",
                new List<Guid>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_ThrowsInvalidOperation_WhenStaffIdTakenByOtherStaff()
    {
        // Changing to another staff member's ID must be blocked to prevent collision.
        await using var db = CreateDb();
        var alice = MakeStaff("S001", email: "alice@test.com");
        var bob   = MakeStaff("S002", email: "bob@test.com");
        db.Staff.Add(alice);
        db.Staff.Add(bob);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        // Try to give Alice the same StaffId as Bob
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateStaffCommand(
                alice.Id, "S002", "Alice", "Wong", "alice@test.com",
                new List<Guid>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_ThrowsInvalidOperation_WhenEmailTakenByOtherStaff()
    {
        // Changing to another staff member's email must be blocked.
        await using var db = CreateDb();
        var alice = MakeStaff("S001", email: "alice@test.com");
        var bob   = MakeStaff("S002", email: "bob@test.com");
        db.Staff.Add(alice);
        db.Staff.Add(bob);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        // Try to give Alice the same email as Bob
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateStaffCommand(
                alice.Id, "S001", "Alice", "Wong", "bob@test.com",
                new List<Guid>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_Succeeds_WhenBothIcAndPassportAreNull()
    {
        // IC and Passport are optional on update — both null should succeed.
        await using var db = CreateDb();
        var existing = MakeStaff();
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        await handler.Handle(new UpdateStaffCommand(
            existing.Id, "S001", "Alice", "Wong", "alice@test.com",
            new List<Guid>(), null, IcNo: null, PassportNo: null, DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        await db.Entry(existing).ReloadAsync();
        Assert.Null(existing.IcNo);
        Assert.Null(existing.PassportNo);
    }

    [Fact]
    public async Task Update_Succeeds_WhenDateOfBirthIsNull()
    {
        // DateOfBirth is now optional on update — null should succeed and clear the field.
        await using var db = CreateDb();
        var existing = MakeStaff();
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        await handler.Handle(new UpdateStaffCommand(
            existing.Id, "S001", "Alice", "Wong", "alice@test.com",
            new List<Guid>(), null, IcNo: "990101-01-1234", DateOfBirth: null),
            CancellationToken.None);

        await db.Entry(existing).ReloadAsync();
        Assert.Null(existing.DateOfBirth);
    }

    [Fact]
    public async Task Update_Succeeds_WithSameStaffIdAndEmail()
    {
        // Updating own record must not false-positive on the uniqueness checks.
        await using var db = CreateDb();
        var existing = MakeStaff("S001", email: "alice@test.com");
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        // Detach so the handler starts with a clean change tracker and can
        // re-load (and re-attach) the entity without a StaffRole identity conflict.
        db.ChangeTracker.Clear();

        var handler = UpdateHandler(db);

        // Same StaffId and email — self-collision must be excluded
        await handler.Handle(new UpdateStaffCommand(
            existing.Id, "S001", "Alice", "Wong", "alice@test.com",
            new List<Guid>(), null,
            IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);
    }

    [Fact]
    public async Task Update_ChangesFirstAndLastName()
    {
        await using var db = CreateDb();
        var existing = MakeStaff("S001", firstName: "Alice", lastName: "Wong", email: "alice@test.com");
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);
        await handler.Handle(new UpdateStaffCommand(
            existing.Id, "S001", "Betty", "Lim", "alice@test.com",
            new List<Guid>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        var updated = await db.Staff.FirstOrDefaultAsync(s => s.Id == existing.Id);
        Assert.Equal("Betty", updated!.FirstName);
        Assert.Equal("Lim",   updated.LastName);
    }

    // ── DeleteStaffCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ThrowsKeyNotFound_WhenStaffDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = DeleteHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteStaffCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ThrowsInvalidOperation_WhenStaffHasShiftHistory()
    {
        // Staff with attendance records must be deactivated, not deleted, to preserve payroll integrity.
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        var workDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var log = E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc-1", "Main Bar", workDate, "18:00", "23:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        var handler = DeleteHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteStaffCommand(staff.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesStaff_WhenNoShiftHistory()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        var handler = DeleteHandler(db);
        await handler.Handle(new DeleteStaffCommand(staff.Id), CancellationToken.None);

        Assert.Equal(0, await db.Staff.CountAsync());
    }

    [Fact]
    public async Task Delete_RemovesRelatedLeaveRequests()
    {
        // Orphaned leave requests must be cleaned up when a staff member is deleted.
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        var leave = E.LeaveRequest.Create(staff.Id, "annual",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(2), null, null, null, null);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();

        var handler = DeleteHandler(db);
        await handler.Handle(new DeleteStaffCommand(staff.Id), CancellationToken.None);

        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    // ── ActivateStaffCommand + DeactivateStaffCommand ─────────────────────────

    [Fact]
    public async Task Activate_ThrowsKeyNotFound_WhenNotFound()
    {
        await using var db = CreateDb();
        var handler = ActivateHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new ActivateStaffCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Activate_SetsIsActive_True()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.Deactivate(); // start inactive
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        var handler = ActivateHandler(db);
        await handler.Handle(new ActivateStaffCommand(staff.Id), CancellationToken.None);

        var updated = await db.Staff.FindAsync(staff.Id);
        Assert.True(updated!.IsActive);
    }

    [Fact]
    public async Task Deactivate_ThrowsKeyNotFound_WhenNotFound()
    {
        await using var db = CreateDb();
        var handler = DeactivateHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeactivateStaffCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_SetsIsActive_False()
    {
        await using var db = CreateDb();
        var staff = MakeStaff(); // IsActive = true after Create
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        var handler = DeactivateHandler(db);
        await handler.Handle(new DeactivateStaffCommand(staff.Id), CancellationToken.None);

        var updated = await db.Staff.FindAsync(staff.Id);
        Assert.False(updated!.IsActive);
    }

    // ── Last-admin lockout guard (deactivate + role removal) ──────────────────

    private static E.Staff MakeAdmin(string staffId, string email, Guid managerRoleId) =>
        E.Staff.Create(staffId, "Meg", "Boss", email, "hash",
            new List<Guid> { managerRoleId }, icNo: staffId, dateOfBirth: new DateTime(1990, 1, 1));

    [Fact]
    public async Task Deactivate_Throws_WhenLastActiveAdmin()
    {
        // Deactivating the only user who can reach the admin portal would lock everyone out.
        await using var db = CreateDb();
        var managerRoleId = await RoleSeed.EnsureRoleAsync(db, "MANAGER");
        db.RolePermissions.Add(E.RolePermission.Create(managerRoleId, AppConstants.Permissions.PortalAdmin));
        var admin = MakeAdmin("MGR1", "meg@test.com", managerRoleId);
        db.Staff.Add(admin);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            DeactivateHandler(db).Handle(new DeactivateStaffCommand(admin.Id), CancellationToken.None));
        Assert.Contains("admin-portal access", ex.Message);

        await db.Entry(admin).ReloadAsync();
        Assert.True(admin.IsActive);   // unchanged — deactivation was refused
    }

    [Fact]
    public async Task Deactivate_Allows_WhenAnotherActiveAdminRemains()
    {
        await using var db = CreateDb();
        var managerRoleId = await RoleSeed.EnsureRoleAsync(db, "MANAGER");
        db.RolePermissions.Add(E.RolePermission.Create(managerRoleId, AppConstants.Permissions.PortalAdmin));
        var a = MakeAdmin("MGR1", "meg@test.com", managerRoleId);
        var b = MakeAdmin("MGR2", "moe@test.com", managerRoleId);
        db.Staff.AddRange(a, b);
        await db.SaveChangesAsync();

        await DeactivateHandler(db).Handle(new DeactivateStaffCommand(a.Id), CancellationToken.None);

        await db.Entry(a).ReloadAsync();
        Assert.False(a.IsActive);   // allowed — b still covers admin access
    }

    [Fact]
    public async Task Update_Throws_WhenRolesChangeRemovesAdminFromLastAdmin()
    {
        // Re-roling the only admin to a non-admin role would lock everyone out.
        await using var db = CreateDb();
        var roles = await RoleSeed.EnsureRolesAsync(db, "MANAGER", "BARTENDER");
        db.RolePermissions.Add(E.RolePermission.Create(roles["MANAGER"], AppConstants.Permissions.PortalAdmin));
        var admin = MakeAdmin("MGR1", "meg@test.com", roles["MANAGER"]);
        db.Staff.Add(admin);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            UpdateHandler(db).Handle(new UpdateStaffCommand(
                admin.Id, "MGR1", "Meg", "Boss", "meg@test.com",
                new List<Guid> { roles["BARTENDER"] }, null, IcNo: "MGR1", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
        Assert.Contains("last administrator", ex.Message);
    }

    [Fact]
    public async Task Update_Allows_RemovingAdminRole_WhenAnotherAdminRemains()
    {
        await using var db = CreateDb();
        var roles = await RoleSeed.EnsureRolesAsync(db, "MANAGER", "BARTENDER");
        db.RolePermissions.Add(E.RolePermission.Create(roles["MANAGER"], AppConstants.Permissions.PortalAdmin));
        var a = MakeAdmin("MGR1", "meg@test.com", roles["MANAGER"]);
        var b = MakeAdmin("MGR2", "moe@test.com", roles["MANAGER"]);
        db.Staff.AddRange(a, b);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await UpdateHandler(db).Handle(new UpdateStaffCommand(
            a.Id, "MGR1", "Meg", "Boss", "meg@test.com",
            new List<Guid> { roles["BARTENDER"] }, null, IcNo: "MGR1", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        // Reaching here without an ArgumentException is the assertion: the guard ALLOWED the role
        // change because admin b still covers admin-portal access. (Role reconciliation itself is
        // exercised by Update_ChangesFirstAndLastName / the resolver tests.)
        var still = await db.Staff.FindAsync(a.Id);
        Assert.NotNull(still);
        Assert.True(still!.IsActive);
    }
}
