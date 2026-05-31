using System.Net;
using System.Net.Http.Json;
using HRNexus.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Drives the real HTTP pipeline end-to-end (the layer the handler unit tests never reach):
/// exception→status mapping, the per-account lockout returning 429, and the per-IP flood limiter.
/// Each test uses a distinct X-Test-Ip so the rate-limit partitions don't bleed across tests.
/// </summary>
[Collection("postgres")]
public class HttpAuthTests(PostgresFixture fx) : IAsyncLifetime
{
    private TestApiFactory _factory = default!;

    public Task InitializeAsync()
    {
        _factory = new TestApiFactory(fx.ConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedLoginAccountAsync(string staffId, string password)
    {
        await using var db = fx.NewDb();
        if (await db.Staff.AnyAsync(s => s.StaffId == staffId)) return;
        if (!await db.Roles.AnyAsync(r => r.Name == "BARTENDER"))
            db.Roles.Add(E.Role.Create("bartender", hourlyRate: 20m));
        var hasher = new PasswordHasher();   // BCrypt — must match the API's real hasher
        db.Staff.Add(E.Staff.Create(staffId, "Http", "Test", $"{staffId}@t.com",
            hasher.Hash(password), new List<string> { "BARTENDER" }));
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> LoginAsync(string staffId, string password, string ip)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId, password, portal = (string?)null }),
        };
        req.Headers.Add("X-Test-Ip", ip);
        return await _factory.CreateClient().SendAsync(req);
    }

    [Fact]
    public async Task Login_Returns200_WithValidSeededCredentials()
    {
        // HRADMIN/Admin@1234 is seeded by SeedDemoUsersAsync on startup.
        var resp = await LoginAsync("HRADMIN", "Admin@1234", "10.0.0.1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Login_Returns401_WithWrongPassword()
    {
        await SeedLoginAccountAsync("WRONGPW", "Correct@123");
        var resp = await LoginAsync("WRONGPW", "definitely-wrong", "10.0.0.2");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_Returns429_AndStaysLocked_AfterFiveFailedAttempts()
    {
        await SeedLoginAccountAsync("LOCKTEST", "Correct@123");
        const string ip = "10.0.0.3";

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 5; i++)
            last = (await LoginAsync("LOCKTEST", "wrong", ip)).StatusCode;

        // The attempt that crosses the threshold flips from 401 to 429 (account locked).
        Assert.Equal(HttpStatusCode.TooManyRequests, last);

        // Even the CORRECT password is now refused with 429 while the lockout window holds.
        var correct = await LoginAsync("LOCKTEST", "Correct@123", ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
    }

    [Fact]
    public async Task Login_SetsHttpOnlyAuthCookies_NoBodyTokens_AndCookieAuthenticates()
    {
        await SeedLoginAccountAsync("COOKIETEST", "Correct@123");
        const string ip = "10.0.0.20";
        var client = _factory.CreateClient();   // Mvc.Testing client auto-persists Set-Cookie

        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId = "COOKIETEST", password = "Correct@123", portal = (string?)null }),
        };
        login.Headers.Add("X-Test-Ip", ip);
        var loginResp = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        // Both auth cookies are set and HttpOnly (JS/XSS can't read them).
        var setCookies = loginResp.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList() : new List<string>();
        Assert.Contains(setCookies, c => c.StartsWith("hrnexus_at=", StringComparison.Ordinal)
            && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookies, c => c.StartsWith("hrnexus_rt=", StringComparison.Ordinal)
            && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        // The response body must NOT carry the tokens any more.
        var body = await loginResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);

        // The cookie alone authenticates a protected endpoint (no Authorization header sent).
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("X-Test-Ip", ip);
        var meResp = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
    }

    [Fact]
    public async Task Logout_ExpiresAuthCookies_AndClearsRefreshToken()
    {
        await SeedLoginAccountAsync("LOGOUTTEST", "Correct@123");
        const string ip = "10.0.0.21";
        var client = _factory.CreateClient();

        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId = "LOGOUTTEST", password = "Correct@123", portal = (string?)null }),
        };
        login.Headers.Add("X-Test-Ip", ip);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-Test-Ip", ip);
        var logoutResp = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResp.StatusCode);

        // Cookies are expired (emitted with an empty value).
        var setCookies = logoutResp.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList() : new List<string>();
        Assert.Contains(setCookies, c => c.StartsWith("hrnexus_at=;", StringComparison.Ordinal));
        Assert.Contains(setCookies, c => c.StartsWith("hrnexus_rt=;", StringComparison.Ordinal));

        // The server-side refresh token is gone, so the session can't be resurrected.
        await using var db = fx.NewDb();
        var refresh = await db.Staff.Where(s => s.StaffId == "LOGOUTTEST")
            .Select(s => s.RefreshToken).FirstOrDefaultAsync();
        Assert.Null(refresh);
    }

    [Fact]
    public async Task AuthEndpoint_Returns429_WhenIpFloodsBeyondWindowLimit()
    {
        // PermitLimit is 30/min per IP. Fire 40 from one (unique) IP; the overflow must be 429.
        // Bogus, non-existent account so there is no per-account lockout side effect.
        const string ip = "10.0.0.4";
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 40; i++)
            statuses.Add((await LoginAsync("FLOODBOT", "x", ip)).StatusCode);

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
