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
/// Handler-level tests for Auth commands — Login, ForgotPassword, RefreshToken.
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

    // Seeds the named role rows (defaulting to BARTENDER) so the Role navigation resolves under the
    // InMemory provider — the login handler reads role NAMES back through Staff.Roles for the User DTO.
    private static async Task<E.Staff> MakeStaff(
        TestDbContext db,
        string staffId = "S001",
        string password = "pass",
        params string[] roleNames)
    {
        var hasher = new StubHasher();
        var names = roleNames.Length > 0 ? roleNames : ["BARTENDER"];
        var ids = new List<Guid>();
        foreach (var name in names) ids.Add(await RoleSeed.EnsureRoleAsync(db, name));
        return E.Staff.Create(staffId, "Alice", "Wong", "alice@test.com",
            hasher.Hash(password), ids);
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
        db.Staff.Add(await MakeStaff(db, password: "correct"));
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
        db.Staff.Add(await MakeStaff(db, roleNames: "SUPERADMIN"));
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
        db.Staff.Add(await MakeStaff(db, roleNames: "BARTENDER"));
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
        db.Staff.Add(await MakeStaff(db, roleNames: "MANAGER"));
        // MANAGER role grants admin-portal access (matches the seeded backfill). The permission row
        // must key on the SAME role id the resolver joins by, so resolve the seeded MANAGER role's id.
        var managerRoleId = await RoleSeed.RoleIdAsync(db, "MANAGER");
        db.RolePermissions.Add(E.RolePermission.Create(managerRoleId, AppConstants.Permissions.PortalAdmin));
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
        db.Staff.Add(await MakeStaff(db, roleNames: "BARTENDER"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeLoginHandler(db);
        var result = await handler.Handle(new LoginCommand("S001", "pass", Portal: "staff"), CancellationToken.None);

        Assert.Equal("stub-access", result.AccessToken);
        Assert.Contains("BARTENDER", result.User.Roles);
    }

    [Fact]
    public async Task Login_UserJobs_CarryPayType()
    {
        // The staff-app log-shift check-out picker sizes its minute step from each job's pay type
        // (fixed = 10-min, hourly = 30-min), so the login payload must expose it per job.
        await using var db = CreateDb();
        db.Staff.Add(await MakeStaff(db, roleNames: "BARTENDER"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeLoginHandler(db);
        var result  = await handler.Handle(new LoginCommand("S001", "pass", Portal: "staff"), CancellationToken.None);

        var job = Assert.Single(result.User.Jobs);
        Assert.False(string.IsNullOrWhiteSpace(job.PayType));   // e.g. "hourly"
    }

    // ── LoginCommand: brute-force lockout ─────────────────────────────────────
    // WHY: without a lockout an attacker can try unlimited passwords against one account.
    // After N consecutive failures the account must refuse logins (even with the CORRECT
    // password) for a cooldown, then recover automatically once the window passes.

    [Fact]
    public async Task Login_LocksAccount_AfterMaxFailedAttempts_ThenRefusesEvenCorrectPassword()
    {
        await using var db = CreateDb();
        db.Staff.Add(await MakeStaff(db, password: "correct"));
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
        db.Staff.Add(await MakeStaff(db, password: "correct"));
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
        db.Staff.Add(await MakeStaff(db, password: "correct"));
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
        staff.SetRefreshToken("expired-token", DateTime.UtcNow.AddDays(-1));
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RefreshTokenCommandHandler(db, new StubTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new RefreshTokenCommand("expired-token"), CancellationToken.None));
    }

    [Fact]
    public async Task RefreshToken_Succeeds_KeepsSameTokenAndSlidesExpiry()
    {
        // The refresh token is deliberately NOT rotated: a lost rotation response used to strand the
        // client with a token the server had already replaced, forcing a spurious re-login. The token
        // stays the same and its 30-day expiry slides forward on each use.
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var partialExpiry = DateTime.UtcNow.AddDays(5);   // a partly-used 30-day window
        staff.SetRefreshToken("valid-token", partialExpiry);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RefreshTokenCommandHandler(db, new StubTokenService());
        var result = await handler.Handle(new RefreshTokenCommand("valid-token"), CancellationToken.None);

        Assert.Equal("stub-access", result.AccessToken);
        Assert.Equal("valid-token", result.RefreshToken);   // same token, not rotated

        var updated = await db.Staff.FirstAsync(e => e.Id == staff.Id);
        Assert.Equal("valid-token", updated.RefreshToken);
        Assert.True(updated.RefreshTokenExpiry > partialExpiry.AddDays(20));   // expiry slid ~30 days out
    }

    [Fact]
    public async Task RefreshToken_ConcurrentRefresh_IsBenign_KeepsSessionAlive()
    {
        // Two concurrent refreshes both present the same (valid, unexpired) token. One save wins; the
        // other gets DbUpdateConcurrencyException. Now that the token is NOT rotated (only its expiry
        // slides), the conflict is harmless — the token is unchanged and still valid — so the handler
        // swallows it and returns a fresh access token instead of forcing the user to sign in again.
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ConcurrentRotationDb(opts);

        // Build the staff directly (NOT via MakeStaff, which seeds a role with its own SaveChanges and
        // would consume the ConcurrentRotationDb's one-time first-save slot). The refresh handler never
        // reads role names, so an unresolved role id is fine here.
        var staff = E.Staff.Create("S001", "Alice", "Wong", "alice@test.com",
            new StubHasher().Hash("pass"), new List<Guid>());
        staff.SetRefreshToken("valid-token", DateTime.UtcNow.AddDays(30));
        db.Staff.Add(staff);
        // First SaveChangesAsync — seeds the data via ConcurrentRotationDb's seeding path
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RefreshTokenCommandHandler(db, new StubTokenService());

        // Handler's SaveChangesAsync (the second call) throws DbUpdateConcurrencyException, which the
        // handler now swallows → the refresh still succeeds and the session stays alive.
        var result = await handler.Handle(new RefreshTokenCommand("valid-token"), CancellationToken.None);
        Assert.Equal("stub-access", result.AccessToken);
        Assert.Equal("valid-token", result.RefreshToken);
    }

}
