using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Auth.Commands;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Application.Tests.Infrastructure;
using E = NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

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
    public async Task Login_ThrowsUnauthorized_WhenAdminAccessesStaffPortal()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(roles: new List<string> { "ADMIN" }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new LoginCommand("S001", "pass", Portal: "staff"), CancellationToken.None));
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
        Assert.Contains("bartender", result.User.Roles);
    }

    [Fact]
    public async Task Login_Succeeds_ReturnsMustChangePassword_WhenFlagSet()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.SetMustChangePassword();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new LoginCommandHandler(db, new StubHasher(), new StubTokenService());
        var result = await handler.Handle(new LoginCommand("S001", "pass"), CancellationToken.None);

        Assert.True(result.MustChangePassword);
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
    public async Task ChangePassword_Succeeds_ClearsRefreshToken()
    {
        await using var db = CreateDb();
        var staff = MakeStaff(password: "pass");
        staff.SetRefreshToken("old-token", DateTime.UtcNow.AddDays(1));
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ChangePasswordCommandHandler(db, new StubHasher());
        await handler.Handle(new ChangePasswordCommand(staff.Id, "pass", "newpass12"),
            CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Null(updated.RefreshToken);
        Assert.Equal("hashed-newpass12", updated.PasswordHash);
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

    // ── ForceChangePasswordCommand ────────────────────────────────────────────

    [Fact]
    public async Task ForceChangePassword_ThrowsKeyNotFound_WhenEmployeeDoesNotExist()
    {
        await using var db = CreateDb();
        var handler = new ForceChangePasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new ForceChangePasswordCommand(Guid.NewGuid(), "newpass12"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ForceChangePassword_ThrowsArgument_WhenNewPasswordTooShort()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        var handler = new ForceChangePasswordCommandHandler(db, new StubHasher());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ForceChangePasswordCommand(staff.Id, "short"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ForceChangePassword_Succeeds_ClearsMustChangePasswordFlag()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.SetMustChangePassword();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ForceChangePasswordCommandHandler(db, new StubHasher());
        await handler.Handle(new ForceChangePasswordCommand(staff.Id, "newpass12"),
            CancellationToken.None);

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.False(updated.MustChangePassword);
        Assert.Equal("hashed-newpass12", updated.PasswordHash);
    }

    // ── RefreshTokenCommand ───────────────────────────────────────────────────

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
}
