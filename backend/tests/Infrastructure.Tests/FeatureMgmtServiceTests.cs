using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Infrastructure.Auth;
using Xunit;

namespace HRNexus.Infrastructure.Tests;

/// <summary>
/// Unit tests for FeatureMgmtService.
/// Uses a fast fake hasher (no BCrypt) and in-memory IConfiguration.
/// Each test gets a unique IP to avoid static rate-limit cross-contamination.
/// </summary>
public class FeatureMgmtServiceTests
{
    // ── Fake hasher (avoids slow BCrypt in unit tests) ────────────────────────

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password)              => "HASHED:" + password;
        public bool   Verify(string password, string hash) => hash == "HASHED:" + password;
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    private const string TestSecret = "UnitTestSecret_MustBe32PlusChars!!!";
    private const string TestPin    = "AdminPin99!";
    private const string WrongPin   = "WrongPin00!";
    private const string TestHash   = "HASHED:" + TestPin;   // compile-time constant (string concat)

    // ── Factory helpers ───────────────────────────────────────────────────────

    private static FeatureMgmtService CreateSvc(
        string? pinHash     = TestHash,
        string? tokenSecret = TestSecret)
    {
        var data = new Dictionary<string, string?>();
        if (pinHash     != null) data["FeatureMgmt:PinHash"]     = pinHash;
        if (tokenSecret != null) data["FeatureMgmt:TokenSecret"] = tokenSecret;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        return new FeatureMgmtService(config, new FakeHasher());
    }

    /// <summary>Unique GUID as IP ensures no cross-test rate-limit bleed.</summary>
    private static string UniqueIp() => Guid.NewGuid().ToString();

    // ── IsPinConfigured ───────────────────────────────────────────────────────

    [Fact]
    public void IsPinConfigured_ReturnsFalse_WhenKeyMissing()
    {
        var svc = CreateSvc(pinHash: null);
        Assert.False(svc.IsPinConfigured);
    }

    [Fact]
    public void IsPinConfigured_ReturnsFalse_WhenEmpty()
    {
        var svc = CreateSvc(pinHash: "");
        Assert.False(svc.IsPinConfigured);
    }

    [Fact]
    public void IsPinConfigured_ReturnsTrue_WhenHashSet()
    {
        var svc = CreateSvc();
        Assert.True(svc.IsPinConfigured);
    }

    // ── HashPin ───────────────────────────────────────────────────────────────

    [Fact]
    public void HashPin_DelegatesToHasher()
    {
        var svc  = CreateSvc();
        var hash = svc.HashPin("mypin");
        Assert.Equal("HASHED:mypin", hash);
    }

    // ── TryUnlock — success path ──────────────────────────────────────────────

    [Fact]
    public void TryUnlock_CorrectPin_ReturnsSuccess()
    {
        var svc    = CreateSvc();
        var result = svc.TryUnlock(TestPin, UniqueIp());
        Assert.Equal(UnlockStatus.Success, result.Status);
    }

    [Fact]
    public void TryUnlock_CorrectPin_ReturnsNonEmptyToken()
    {
        var svc    = CreateSvc();
        var result = svc.TryUnlock(TestPin, UniqueIp());
        Assert.NotNull(result.Token);
        Assert.NotEmpty(result.Token!);
    }

    [Fact]
    public void TryUnlock_CorrectPin_ExpiresInApproximately4Hours()
    {
        var svc    = CreateSvc();
        var before = DateTime.UtcNow.AddHours(4).AddSeconds(-5);
        var result = svc.TryUnlock(TestPin, UniqueIp());
        var after  = DateTime.UtcNow.AddHours(4).AddSeconds(5);

        Assert.NotNull(result.ExpiresAt);
        Assert.InRange(result.ExpiresAt!.Value, before, after);
    }

    // ── TryUnlock — wrong PIN ─────────────────────────────────────────────────

    [Fact]
    public void TryUnlock_WrongPin_ReturnsInvalidPin()
    {
        var svc    = CreateSvc();
        var result = svc.TryUnlock(WrongPin, UniqueIp());
        Assert.Equal(UnlockStatus.InvalidPin, result.Status);
    }

    [Fact]
    public void TryUnlock_WrongPin_NoTokenIssued()
    {
        var svc    = CreateSvc();
        var result = svc.TryUnlock(WrongPin, UniqueIp());
        Assert.Null(result.Token);
    }

    [Fact]
    public void TryUnlock_NoPinConfigured_TreatsAsInvalidPin()
    {
        // If hash is empty, Verify() will always fail → InvalidPin
        var svc    = CreateSvc(pinHash: "");
        var result = svc.TryUnlock(TestPin, UniqueIp());
        Assert.Equal(UnlockStatus.InvalidPin, result.Status);
    }

    // ── TryUnlock — rate limiting ─────────────────────────────────────────────

    [Fact]
    public void TryUnlock_FiveWrongAttempts_NextCallIsLockedOut()
    {
        var svc = CreateSvc();
        var ip  = UniqueIp();

        for (int i = 0; i < 5; i++)
            svc.TryUnlock(WrongPin, ip);

        var result = svc.TryUnlock(WrongPin, ip);
        Assert.Equal(UnlockStatus.LockedOut, result.Status);
    }

    [Fact]
    public void TryUnlock_LockedOut_RetryAfterIsInFuture()
    {
        var svc = CreateSvc();
        var ip  = UniqueIp();

        for (int i = 0; i < 5; i++)
            svc.TryUnlock(WrongPin, ip);

        var result = svc.TryUnlock(WrongPin, ip);
        Assert.Equal(UnlockStatus.LockedOut, result.Status);
        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter!.Value > DateTime.UtcNow);
    }

    [Fact]
    public void TryUnlock_FifthBadAttempt_ReturnsInvalidPin_LockAppliesNextCall()
    {
        // The lockout only BLOCKS from the call AFTER the 5th failure.
        // The 5th failure itself still returns InvalidPin (lock is set for next time).
        var svc = CreateSvc();
        var ip  = UniqueIp();

        for (int i = 0; i < 4; i++)
            svc.TryUnlock(WrongPin, ip);

        var fifthResult = svc.TryUnlock(WrongPin, ip);
        Assert.Equal(UnlockStatus.InvalidPin, fifthResult.Status);
    }

    [Fact]
    public void TryUnlock_CorrectPin_ClearsRateLimitCounter()
    {
        var svc = CreateSvc();
        var ip  = UniqueIp();

        // Build up 4 failures
        for (int i = 0; i < 4; i++)
            svc.TryUnlock(WrongPin, ip);

        // Correct PIN → clears counter
        var ok = svc.TryUnlock(TestPin, ip);
        Assert.Equal(UnlockStatus.Success, ok.Status);

        // Should be able to fail 4 more times without lockout
        for (int i = 0; i < 4; i++)
            svc.TryUnlock(WrongPin, ip);

        // 5th failure after reset → lockout NOT triggered yet on this call
        var fifthAfterReset = svc.TryUnlock(WrongPin, ip);
        Assert.Equal(UnlockStatus.InvalidPin, fifthAfterReset.Status);
    }

    [Fact]
    public void TryUnlock_DifferentIps_RateLimitsAreIndependent()
    {
        var svc = CreateSvc();
        var ip1 = UniqueIp();
        var ip2 = UniqueIp();

        // Lock ip1
        for (int i = 0; i < 5; i++)
            svc.TryUnlock(WrongPin, ip1);
        svc.TryUnlock(WrongPin, ip1); // this one is LockedOut

        // ip2 should be completely unaffected
        var result = svc.TryUnlock(WrongPin, ip2);
        Assert.Equal(UnlockStatus.InvalidPin, result.Status);
    }

    // ── ValidateToken ─────────────────────────────────────────────────────────

    [Fact]
    public void ValidateToken_Null_ReturnsFalse()
    {
        Assert.False(CreateSvc().ValidateToken(null));
    }

    [Fact]
    public void ValidateToken_Empty_ReturnsFalse()
    {
        Assert.False(CreateSvc().ValidateToken(""));
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsTrue()
    {
        var svc    = CreateSvc();
        var result = svc.TryUnlock(TestPin, UniqueIp());
        Assert.True(svc.ValidateToken(result.Token));
    }

    [Fact]
    public void ValidateToken_TamperedSignature_ReturnsFalse()
    {
        var svc    = CreateSvc();
        var result = svc.TryUnlock(TestPin, UniqueIp());
        // Append a character to corrupt the signature segment
        var tampered = result.Token! + "X";
        Assert.False(svc.ValidateToken(tampered));
    }

    [Fact]
    public void ValidateToken_ExpiredToken_ReturnsFalse()
    {
        // Build a token manually with expiry in the past
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt   = new JwtSecurityToken(
            issuer:             "nc-feature-mgmt",
            audience:           "nc-feature-mgmt",
            claims:             [new Claim("purpose", "feature-access")],
            expires:            DateTime.UtcNow.AddSeconds(-10),
            signingCredentials: creds);
        var expiredToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        Assert.False(CreateSvc().ValidateToken(expiredToken));
    }

    [Fact]
    public void ValidateToken_WrongIssuer_ReturnsFalse()
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt   = new JwtSecurityToken(
            issuer:             "wrong-issuer",
            audience:           "nc-feature-mgmt",
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        Assert.False(CreateSvc().ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_WrongAudience_ReturnsFalse()
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt   = new JwtSecurityToken(
            issuer:             "nc-feature-mgmt",
            audience:           "wrong-audience",
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        Assert.False(CreateSvc().ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_WrongSigningSecret_ReturnsFalse()
    {
        var wrongKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("DifferentSecret_MustBe32PlusChars!!!"));
        var creds = new SigningCredentials(wrongKey, SecurityAlgorithms.HmacSha256);
        var jwt   = new JwtSecurityToken(
            issuer:             "nc-feature-mgmt",
            audience:           "nc-feature-mgmt",
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        Assert.False(CreateSvc().ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_MissingTokenSecret_ReturnsFalse()
    {
        // No TokenSecret configured → ValidateToken should bail out safely
        var svc = CreateSvc(tokenSecret: null);
        Assert.False(svc.ValidateToken("any.random.token"));
    }

    [Fact]
    public void ValidateToken_RandomJunk_ReturnsFalse()
    {
        Assert.False(CreateSvc().ValidateToken("not.a.jwt"));
    }
}
