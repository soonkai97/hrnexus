using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Auth.Commands;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using HRNexus.Infrastructure.Auth;
using Microsoft.Extensions.Caching.Memory;
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
        public string GenerateTwoFactorToken(Guid staffId) => $"stub-2fa:{staffId}";
        public Guid? ValidateTwoFactorToken(string t) =>
            t.StartsWith("stub-2fa:") && Guid.TryParse(t["stub-2fa:".Length..], out var id) ? id : null;
    }

    /// <summary>Deterministic TOTP stub: the secret is fixed, "GOODCODE" verifies, recovery codes
    /// are predictable, and hashing is the identity-ish "h:" prefix so tests can assert on them.</summary>
    private sealed class StubTotp : ITotpService
    {
        public string GenerateSecret() => "STUBSECRET234567";
        public string BuildOtpAuthUri(string secret, string account, string issuer) => $"otpauth://totp/{issuer}:{account}?secret={secret}";
        public string BuildQrPngDataUri(string uri) => "data:image/png;base64,STUB";
        public bool VerifyCode(string secret, string code) => code == "GOODCODE";
        public IReadOnlyList<string> GenerateRecoveryCodes(int count = 10) =>
            Enumerable.Range(0, count).Select(i => $"RECOV-{i:D5}").ToList();
        public string HashRecoveryCode(string code) => "h:" + new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private sealed class NullNotificationHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default) => Task.CompletedTask;
        public Task BroadcastLiveStatsAsync(LiveStatsDto s, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Mutable clock so lockout-window tests can advance time deterministically.</summary>
    private sealed class StubClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => UtcNow;
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

    private static PermissionResolver MakeResolver(TestDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), new PermissionCacheState());

    /// <summary>Login handler wired with the REAL permission resolver, so the admin-portal gate
    /// reflects actual portal.admin permissions in the test DB.</summary>
    private static LoginCommandHandler MakeLoginHandler(TestDbContext db, IDateTimeProvider? clock = null) =>
        new(db, new StubHasher(), new StubTokenService(), clock ?? new StubClock(), MakeResolver(db));

    // ── LoginCommand ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ThrowsUnauthorized_WhenStaffNotFound()
    {
        await using var db = CreateDb();
        var handler = MakeLoginHandler(db);

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

        var handler = MakeLoginHandler(db);

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

        var handler = MakeLoginHandler(db);

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

        var handler = MakeLoginHandler(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new LoginCommand("S001", "pass", Portal: "admin"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_Succeeds_WhenManagerAccessesAdminPortal()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(roles: new List<string> { "MANAGER" }));
        // MANAGER role grants admin-portal access (matches the seeded backfill).
        db.RolePermissions.Add(E.RolePermission.Create("MANAGER", AppConstants.Permissions.PortalAdmin));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeLoginHandler(db);
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

        var handler = MakeLoginHandler(db);
        var result = await handler.Handle(new LoginCommand("S001", "pass", Portal: "staff"), CancellationToken.None);

        Assert.Equal("stub-access", result.AccessToken);
        Assert.Contains("BARTENDER", result.User.Roles);
    }

    // ── LoginCommand: brute-force lockout ─────────────────────────────────────
    // WHY: without a lockout an attacker can try unlimited passwords against one account.
    // After N consecutive failures the account must refuse logins (even with the CORRECT
    // password) for a cooldown, then recover automatically once the window passes.

    [Fact]
    public async Task Login_LocksAccount_AfterMaxFailedAttempts_ThenRefusesEvenCorrectPassword()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(password: "correct"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var clock   = new StubClock();
        var handler = MakeLoginHandler(db, clock);

        // First (MaxFailedAttempts - 1) failures return the generic 401…
        for (var i = 0; i < AppConstants.Lockout.MaxFailedAttempts - 1; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.Handle(new LoginCommand("S001", "wrong"), CancellationToken.None));

        // …the failure that hits the threshold flips to 429 (TooManyRequests).
        await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            handler.Handle(new LoginCommand("S001", "wrong"), CancellationToken.None));

        // Now even the CORRECT password is refused with 429 while the lockout holds.
        await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            handler.Handle(new LoginCommand("S001", "correct"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_Recovers_AfterLockoutWindowExpires()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(password: "correct"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var clock   = new StubClock();
        var handler = MakeLoginHandler(db, clock);

        for (var i = 0; i < AppConstants.Lockout.MaxFailedAttempts; i++)
            await Assert.ThrowsAnyAsync<Exception>(() =>
                handler.Handle(new LoginCommand("S001", "wrong"), CancellationToken.None));

        // Advance past the lockout window — the correct password now succeeds.
        clock.UtcNow = clock.UtcNow.Add(AppConstants.Lockout.Duration).AddMinutes(1);
        var result = await handler.Handle(new LoginCommand("S001", "correct"), CancellationToken.None);
        Assert.Equal("stub-access", result.AccessToken);
    }

    [Fact]
    public async Task Login_ResetsFailedCount_OnSuccessfulLogin()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff(password: "correct"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var clock   = new StubClock();
        var handler = MakeLoginHandler(db, clock);

        // A couple of failures (below threshold) then a success must clear the counter,
        // so a later failure starts a fresh window rather than tipping into a lockout.
        for (var i = 0; i < AppConstants.Lockout.MaxFailedAttempts - 1; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.Handle(new LoginCommand("S001", "wrong"), CancellationToken.None));

        await handler.Handle(new LoginCommand("S001", "correct"), CancellationToken.None);

        var staff = await db.Staff.FirstAsync();
        Assert.Equal(0, staff.AccessFailedCount);
        Assert.Null(staff.LockoutEnd);

        // One more wrong attempt should be a plain 401, not a lockout.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new LoginCommand("S001", "wrong"), CancellationToken.None));
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

    // ── Two-factor authentication ─────────────────────────────────────────────
    // WHY: 2FA must gate the admin portal (a correct password alone returns a challenge, NOT a
    // session), enrollment must verify the first code before enabling, and verification must accept
    // a valid TOTP code OR a single-use recovery code while rejecting wrong codes.

    private static E.Staff MakeAdminWith2fa(StubTotp totp)
    {
        var staff = MakeStaff(roles: new List<string> { "MANAGER" });
        staff.BeginTwoFactorEnrollment(totp.GenerateSecret());
        staff.EnableTwoFactor(totp.GenerateRecoveryCodes().Select(totp.HashRecoveryCode));
        return staff;
    }

    [Fact]
    public async Task Login_ReturnsTwoFactorChallenge_WhenAdminPortalAnd2faEnabled()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeAdminWith2fa(new StubTotp()));
        db.RolePermissions.Add(E.RolePermission.Create("MANAGER", AppConstants.Permissions.PortalAdmin));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await MakeLoginHandler(db).Handle(
            new LoginCommand("S001", "pass", Portal: "admin"), CancellationToken.None);

        Assert.True(result.RequiresTwoFactor);
        Assert.Equal("", result.AccessToken);          // no session tokens yet
        Assert.False(string.IsNullOrEmpty(result.TwoFactorToken));
    }

    [Fact]
    public async Task Login_SkipsTwoFactor_OnStaffPortal_EvenWhenEnabled()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeAdminWith2fa(new StubTotp()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await MakeLoginHandler(db).Handle(
            new LoginCommand("S001", "pass", Portal: "staff"), CancellationToken.None);

        Assert.False(result.RequiresTwoFactor);
        Assert.Equal("stub-access", result.AccessToken);
    }

    [Fact]
    public async Task BeginEnroll_StoresPendingSecret_NotYetEnabled()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff());
        await db.SaveChangesAsync();
        var id = db.Staff.Single().Id;
        db.ChangeTracker.Clear();

        var dto = await new BeginTwoFactorEnrollHandler(db, new StubTotp())
            .Handle(new BeginTwoFactorEnrollCommand(id), CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(dto.Secret));
        var staff = await db.Staff.FindAsync(id);
        Assert.False(staff!.TwoFactorEnabled);
        Assert.Equal(dto.Secret, staff.TwoFactorSecret);
    }

    [Fact]
    public async Task ConfirmEnroll_WithGoodCode_EnablesAndReturnsRecoveryCodes()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.BeginTwoFactorEnrollment(new StubTotp().GenerateSecret());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dto = await new ConfirmTwoFactorEnrollHandler(db, new StubTotp())
            .Handle(new ConfirmTwoFactorEnrollCommand(staff.Id, "GOODCODE"), CancellationToken.None);

        Assert.NotEmpty(dto.RecoveryCodes);
        Assert.True((await db.Staff.FindAsync(staff.Id))!.TwoFactorEnabled);
    }

    [Fact]
    public async Task ConfirmEnroll_WithBadCode_Throws_AndStaysDisabled()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        staff.BeginTwoFactorEnrollment(new StubTotp().GenerateSecret());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ConfirmTwoFactorEnrollHandler(db, new StubTotp())
                .Handle(new ConfirmTwoFactorEnrollCommand(staff.Id, "WRONG"), CancellationToken.None));
        Assert.False((await db.Staff.FindAsync(staff.Id))!.TwoFactorEnabled);
    }

    [Fact]
    public async Task VerifyTwoFactor_WithGoodCode_IssuesTokens()
    {
        await using var db = CreateDb();
        var staff = MakeAdminWith2fa(new StubTotp());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new VerifyTwoFactorHandler(db, new StubTotp(), new StubTokenService(), new StubClock())
            .Handle(new VerifyTwoFactorCommand(staff.Id, "GOODCODE"), CancellationToken.None);

        Assert.Equal("stub-access", result.AccessToken);
        Assert.False(result.RequiresTwoFactor);
    }

    [Fact]
    public async Task VerifyTwoFactor_WithRecoveryCode_IssuesTokens_AndConsumesIt()
    {
        await using var db = CreateDb();
        var totp = new StubTotp();
        var staff = MakeAdminWith2fa(totp);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var firstRecovery = totp.GenerateRecoveryCodes()[0];   // "RECOV-00000"
        var result = await new VerifyTwoFactorHandler(db, totp, new StubTokenService(), new StubClock())
            .Handle(new VerifyTwoFactorCommand(staff.Id, firstRecovery), CancellationToken.None);
        Assert.Equal("stub-access", result.AccessToken);

        // The consumed code can't be reused.
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new VerifyTwoFactorHandler(db, totp, new StubTokenService(), new StubClock())
                .Handle(new VerifyTwoFactorCommand(staff.Id, firstRecovery), CancellationToken.None));
    }

    [Fact]
    public async Task VerifyTwoFactor_WithBadCode_Throws()
    {
        await using var db = CreateDb();
        var staff = MakeAdminWith2fa(new StubTotp());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new VerifyTwoFactorHandler(db, new StubTotp(), new StubTokenService(), new StubClock())
                .Handle(new VerifyTwoFactorCommand(staff.Id, "WRONG"), CancellationToken.None));
    }

    [Fact]
    public async Task Disable_WithGoodCode_TurnsOffTwoFactor()
    {
        await using var db = CreateDb();
        var staff = MakeAdminWith2fa(new StubTotp());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new DisableTwoFactorHandler(db, new StubTotp())
            .Handle(new DisableTwoFactorCommand(staff.Id, "GOODCODE"), CancellationToken.None);

        Assert.False((await db.Staff.FindAsync(staff.Id))!.TwoFactorEnabled);
    }

    [Fact]
    public async Task Disable_WithBadCode_Throws_AndStaysEnabled()
    {
        await using var db = CreateDb();
        var staff = MakeAdminWith2fa(new StubTotp());
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new DisableTwoFactorHandler(db, new StubTotp())
                .Handle(new DisableTwoFactorCommand(staff.Id, "WRONG"), CancellationToken.None));
        Assert.True((await db.Staff.FindAsync(staff.Id))!.TwoFactorEnabled);
    }
}
