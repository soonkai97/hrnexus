using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
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
        string email     = "alice@test.com")
        => E.Staff.Create(staffId, firstName, lastName, email, "hash",
            new List<string> { "BARTENDER" },
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
                "", "Alice", "Wong", "alice@test.com", "pass",
                new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
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
                "Alice", "Wong", "alice@test.com", "pass",
                new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenFirstNameIsEmpty()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S001", "", "Wong", "alice@test.com", "pass",
                new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenLastNameIsEmpty()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S001", "Alice", "", "alice@test.com", "pass",
                new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenEmailIsEmpty()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S001", "Alice", "Wong", "", "pass",
                new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenBothIcAndPassportAreEmpty()
    {
        // IC or Passport is required for identity verification; both blank is invalid.
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S001", "Alice", "Wong", "alice@test.com", "pass",
                new List<string>(), IcNo: null, PassportNo: null, DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_ThrowsInvalidOperation_WhenDateOfBirthIsNull()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RegisterStaffCommand(
                "S001", "Alice", "Wong", "alice@test.com", "pass",
                new List<string>(), IcNo: "990101-01-1234", DateOfBirth: null),
                CancellationToken.None));
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
                "Bob", "Tan", "bob@test.com", "pass",
                new List<string>(), IcNo: "990202-02-2345", DateOfBirth: new DateTime(1992, 2, 2)),
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
                "pass", new List<string>(), IcNo: "990202-02-2345", DateOfBirth: new DateTime(1992, 2, 2)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_Succeeds_AndReturnsNewId()
    {
        await using var db = CreateDb();
        var handler = RegisterHandler(db);

        var id = await handler.Handle(new RegisterStaffCommand(
            "S001", "Alice", "Wong", "alice@test.com", "pass",
            new List<string> { "BARTENDER" }, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
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
            "s001", "Alice", "Wong", "alice@test.com", "pass",
            new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
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
            "S001", "Alice", "Wong", "Alice@Test.COM", "pass",
            new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
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
            "S001", "Alice", "Wong", "alice@test.com", "mySecret",
            new List<string>(), IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
            CancellationToken.None);

        var staff = await db.Staff.FindAsync(id);
        // StubHasher produces "hashed-<password>"
        Assert.Equal("hashed-mySecret", staff!.PasswordHash);
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
                new List<string>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_ThrowsArgument_WhenLastNameIsEmpty()
    {
        await using var db = CreateDb();
        var existing = MakeStaff();
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new UpdateStaffCommand(
                existing.Id, "S001", "Alice", "", "alice@test.com",
                new List<string>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_ThrowsKeyNotFound_WhenStaffDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = UpdateHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new UpdateStaffCommand(
                Guid.NewGuid(), "S001", "Alice", "Wong", "alice@test.com",
                new List<string>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
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
                new List<string>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
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
                new List<string>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_ThrowsInvalidOperation_WhenBothIcAndPassportAreEmpty()
    {
        await using var db = CreateDb();
        var existing = MakeStaff();
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateStaffCommand(
                existing.Id, "S001", "Alice", "Wong", "alice@test.com",
                new List<string>(), null, IcNo: null, PassportNo: null, DateOfBirth: new DateTime(1990, 1, 1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_ThrowsInvalidOperation_WhenDateOfBirthIsNull()
    {
        await using var db = CreateDb();
        var existing = MakeStaff();
        db.Staff.Add(existing);
        await db.SaveChangesAsync();

        var handler = UpdateHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateStaffCommand(
                existing.Id, "S001", "Alice", "Wong", "alice@test.com",
                new List<string>(), null, IcNo: "990101-01-1234", DateOfBirth: null),
                CancellationToken.None));
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
            new List<string> { "BARTENDER" }, null,
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
            new List<string>(), null, IcNo: "990101-01-1234", DateOfBirth: new DateTime(1990, 1, 1)),
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
}
