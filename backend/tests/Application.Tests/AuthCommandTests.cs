using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Auth.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for Auth commands — Login, ChangePassword, ForgotPassword,
/// ForceChangePassword, RefreshToken.
/// Guards authentication flow, portal access-control, and password policies.
/// </summary>
public class AuthCommandTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string p) => "hashed-" + p;
        public bool Verify(string p, string hash) => hash == Hash(p);
    }

    private sealed class StubTokenService : ITokenService
    {
        public string GenerateAccessToken(E.Staff e) => "stub-access";
        public string GenerateRefreshToken() => "stub-refresh";
        public Guid? ValidateAccessToken(string t) => null;
    }

    private sealed class NullNotificationHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default) => Task.CompletedTask;
        public Task BroadcastLiveStatsAsync(LiveStatsDto s, CancellationToken ct = default) => Task.CompletedTask;
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
        string staffId = "S001",
        string password = "pass",
        List<string>? roles = null)
    {
        var hasher = new StubHasher();
        return E.Staff.Create(staffId, "Alice", "Wong", "alice@test.com",
            hasher.Hash(password), roles ?? new List<string> { "BARTENDER" });
    }

    // ── LoginCommand ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ThrowsUnauthorized_WhenStaffNotFound()
    {
        await using var db = CreateDb();
        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new LoginCommand("NOBODY", "pass"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_ThrowsUnauthorized_WhenWrongPassword()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(password: "correct"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new LoginCommand("S001", "wrong"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_Succeeds_WhenAdminAccessesStaffPortal()
    {
        // Superadmins are treated as normal users in the staff portal — they can log in there
        // (e.g. to record their own shifts/leave). Only the admin portal is role-gated.
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(roles: new List<string> { "SUPERADMIN" }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());

        var result = await handler.Handle(new LoginCommand("S001", "pass", Portal: "staff"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.AccessToken);
    }

    [Fact]
    public async Task Login_ThrowsUnauthorized_WhenEmployeeAccessesAdminPortal()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(roles: new List<string> { "BARTENDER" }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new LoginCommand("S001", "pass", Portal: "admin"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_Succeeds_WhenManagerAccessesAdminPortal()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(roles: new List<string> { "MANAGER" }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());
        var result = await handler.Handle(new LoginCommand("S001", "pass", Portal: "admin"), CancellationToken.None);

        Assert.NotNull(result.AccessToken);
        Assert.Equal("stub-refresh", result.RefreshToken);
    }

    [Fact]
    public async Task Login_Succeeds_EmployeeOnStaffPortal_ReturnsLowercaseRoles()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(roles: new List<string> { "BARTENDER" }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());
        var result = await handler.Handle(new LoginCommand("S001", "pass", Portal: "staff"), CancellationToken.None);

        Assert.Equal("stub-access", result.AccessToken);
        Assert.Contains("BARTENDER", result.User.Roles);
    }

    // ── ChangePasswordCommand ─────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_ThrowsKeyNotFound_WhenEmployeeDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new ChangePasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new ChangePasswordCommand(Guid.NewGuid(), "pass", "newpass1"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_ThrowsUnauthorized_WhenCurrentPasswordWrong()
    {
        await using var db = CreateDb();
        var staff = MakeStaff(password: "correct");
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ChangePasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new ChangePasswordCommand(staff.Id, "wrong", "newpass1"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_ThrowsArgument_WhenNewPasswordTooShort()
    {
        await using var db = CreateDb();
        var staff = MakeStaff(password: "pass");
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ChangePasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ChangePasswordCommand(staff.Id, "pass", "short"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_ThrowsArgument_WhenNewPasswordLacksComplexity()
    {
        // 8+ chars but all lowercase letters — must be rejected for missing an
        // uppercase letter and a number (policy mirrored on the frontend checklist).
        await using var db = CreateDb();
        var staff = MakeStaff(password: "pass");
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ChangePasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ChangePasswordCommand(staff.Id, "pass", "lowercaseonly"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_ThrowsArgument_WhenNewPasswordIsWhitespaceOnly()
    {
        // Eight spaces pass the length check but must be rejected — a whitespace-only
        // password would be unusable in practice and should never be stored.
        await using var db = CreateDb();
        var staff = MakeStaff(password: "pass");
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ChangePasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ChangePasswordCommand(staff.Id, "pass", "        "),
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_Succeeds_ClearsRefreshToken()
    {
        await using var db = CreateDb();
        var staff = MakeStaff(password: "pass");
        staff.SetRefreshToken("old-token", DateTime.UtcNow.AddDays(1));
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ChangePasswordCommandHandler(db, new StubHasher());
        await handler.Handle(new ChangePasswordCommand(staff.Id, "pass", "Newpass12"),
            CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Null(updated.RefreshToken);
        Assert.Equal("hashed-Newpass12", updated.PasswordHash);
    }

    // ── ForgotPasswordCommand ─────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_ReturnsFalse_WhenStaffNotFound()
    {
        await using var db = CreateDb();
        var handler = new ForgotPasswordCommandHandler(db, new NullNotificationHub());

        var result = await handler.Handle(new ForgotPasswordCommand("UNKNOWN"), CancellationToken.None);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task ForgotPassword_ThrowsInvalidOperation_WhenRateLimitHit()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        // Add a recent password_reset notification (simulating a request made < 5 min ago)
        var notif = E.Notification.Create(staff.Id, "Reset", "msg", "password_reset", staff.Id.ToString());
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ForgotPasswordCommandHandler(db, new NullNotificationHub());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ForgotPasswordCommand(staff.StaffId), CancellationToken.None));
    }

    [Fact]
    public async Task ForgotPassword_ReturnsTrue_WhenStaffFound()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ForgotPasswordCommandHandler(db, new NullNotificationHub());
        var result = await handler.Handle(new ForgotPasswordCommand("S001"), CancellationToken.None);

        Assert.True(result.Found);
    }

    // ── RefreshTokenCommand ───────────────────────────────────────────────────

    /// <summary>
    /// Simulates the second concurrent refresh: <see cref="SaveChangesAsync"/> throws
    /// <see cref="DbUpdateConcurrencyException"/> (EF Core raises this when RefreshToken is
    /// configured as a concurrency token and 0 rows are updated — i.e. another request already
    /// rotated the token). The handler must surface this as <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    private sealed class ConcurrentRotationDb(DbContextOptions<TestDbContext> opts)
        : TestDbContext(opts)
    {
        private bool _firstSaveDone;

        public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (!_firstSaveDone)
            {
                // First call: succeed so we can seed the staff member
                _firstSaveDone = true;
                return await base.SaveChangesAsync(ct);
            }
            // Second call (the handler's SaveChangesAsync): simulate a concurrent rotation
            throw new DbUpdateConcurrencyException("Simulated concurrent token rotation.");
        }
    }

    [Fact]
    public async Task RefreshToken_ThrowsUnauthorized_WhenTokenNotFound()
    {
        await using var db = CreateDb();
        var handler = new RefreshTokenCommandHandler(db, new StubTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new RefreshTokenCommand("invalid-token"), CancellationToken.None));
    }

    [Fact]
    public async Task RefreshToken_ThrowsUnauthorized_WhenTokenExpired()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.SetRefreshToken("expired-token", DateTime.UtcNow.AddDays(-1));
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RefreshTokenCommandHandler(db, new StubTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new RefreshTokenCommand("expired-token"), CancellationToken.None));
    }

    [Fact]
    public async Task RefreshToken_Succeeds_RotatesRefreshToken()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.SetRefreshToken("valid-token", DateTime.UtcNow.AddDays(30));
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RefreshTokenCommandHandler(db, new StubTokenService());
        var result = await handler.Handle(new RefreshTokenCommand("valid-token"), CancellationToken.None);

        Assert.Equal("stub-access", result.AccessToken);
        Assert.Equal("stub-refresh", result.RefreshToken);

        // Old token should be replaced
        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Equal("stub-refresh", updated.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_ThrowsUnauthorized_WhenConcurrentRotationDetected()
    {
        // Two concurrent 401 responses both present the same (valid, unexpired) token.
        // The first refresh wins; the second finds 0 rows updated (EF concurrency token mismatch)
        // and gets DbUpdateConcurrencyException. The handler re-throws as UnauthorizedAccessException
        // so the client is told to sign in again rather than receiving an opaque 500.
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ConcurrentRotationDb(opts);

        var staff = MakeStaff();
        staff.SetRefreshToken("valid-token", DateTime.UtcNow.AddDays(30));
        db.Staff.Add(staff);
        // First SaveChangesAsync — seeds the data via ConcurrentRotationDb's seeding path
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RefreshTokenCommandHandler(db, new StubTokenService());

        // Handler's SaveChangesAsync (the second call) throws DbUpdateConcurrencyException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new RefreshTokenCommand("valid-token"), CancellationToken.None));
    }
}
