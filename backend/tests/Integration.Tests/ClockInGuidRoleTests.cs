using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRNexus.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Regression test for the production bug that motivated the Role GUID primary key: a staff member
/// could NOT clock into a job whose name contained non-ASCII characters (Chinese / full-width parens),
/// because clock-in string-matched the role NAME. With a GUID identity, clock-in resolves the role by
/// its id and never matches a name again.
///
/// This drives the REAL authenticated HTTP endpoint (POST /api/worklogs/start) end-to-end against
/// Postgres, seeding a role literally named 写钟（在外）, assigning it + a location to the staff member,
/// and clocking in using the role's GUID id — asserting success (200).
/// </summary>
[Collection("postgres")]
public class ClockInGuidRoleTests(PostgresFixture fx) : IAsyncLifetime
{
    private const string ChineseRoleName = "写钟（在外）";

    private TestApiFactory _factory = default!;

    public Task InitializeAsync() { _factory = new TestApiFactory(fx.ConnectionString); return Task.CompletedTask; }
    public Task DisposeAsync()    { _factory.Dispose(); return Task.CompletedTask; }

    /// <summary>Seeds the Chinese-named role, a location, and a staff member assigned to BOTH.
    /// Returns the staff's login credentials, the role's GUID id, and the location id.</summary>
    private async Task<(string staffId, string password, Guid roleId, string locationId)> SeedAsync()
    {
        await using var db = fx.NewDb();

        // The role's Name is stored uppercase-normalised, but non-ASCII has no case, so it stays as-is.
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == ChineseRoleName);
        if (role is null)
        {
            role = E.Role.Create(ChineseRoleName, hourlyRate: 30m, nameZh: ChineseRoleName);
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var loc = E.Location.Create($"Venue-{Guid.NewGuid():N}");
        db.Locations.Add(loc);

        var suffix   = Guid.NewGuid().ToString("N");
        var staffId  = $"CN{suffix}"[..10];
        const string password = "Correct@123";
        var hasher = new PasswordHasher();   // BCrypt — must match the API's real hasher
        var staff = E.Staff.Create(staffId, "Chinese", "Role", $"{suffix}@t.com",
            hasher.Hash(password), new List<Guid> { role.Id },
            locations: new List<string> { loc.Id });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        return (staff.StaffId, password, role.Id, loc.Id);
    }

    private async Task<HttpClient> LoginAsync(string staffId, string password, string ip)
    {
        var client = _factory.CreateClient();   // Mvc.Testing client auto-persists Set-Cookie
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId, password, portal = (string?)null }),
        };
        login.Headers.Add("X-Test-Ip", ip);
        var resp = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return client;
    }

    [Fact]
    public async Task ClockIn_Succeeds_ForRoleWithNonAsciiName_UsingGuidId()
    {
        var (staffId, password, roleId, locationId) = await SeedAsync();
        var client = await LoginAsync(staffId, password, "10.0.20.1");

        var start = new HttpRequestMessage(HttpMethod.Post, "/api/worklogs/start")
        {
            Content = JsonContent.Create(new { roleId, locationId, startTime = "20:00", notes = (string?)null }),
        };
        start.Headers.Add("X-Test-Ip", "10.0.20.1");
        var resp = await client.SendAsync(start);

        // Before the GUID fix this path threw because clock-in string-matched the role name; the
        // full-width parens meant the staff member "was not assigned" the role. Now it succeeds.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("id", out var idProp));
        Assert.NotEqual(Guid.Empty, idProp.GetGuid());

        // The shift persisted, snapshotting the role's NAME (frozen display value) into the WorkLog.
        await using var verify = fx.NewDb();
        var log = await verify.WorkLogs.FirstOrDefaultAsync(w => w.Id == idProp.GetGuid());
        Assert.NotNull(log);
        Assert.Equal(ChineseRoleName, log!.RoleName);
        Assert.Equal(locationId, log.LocationId);
    }

    [Fact]
    public async Task ClockIn_Returns200_And_LoginResponseCarriesJobId_ForNonAsciiRole()
    {
        var (staffId, password, roleId, _) = await SeedAsync();
        var client = await LoginAsync(staffId, password, "10.0.20.2");

        // The login response must carry the staff's assigned jobs (id + names) so the staff-app
        // log-shift dropdown can submit the GUID id — verify the Chinese role's id is present.
        var me = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId, password, portal = (string?)null }),
        };
        me.Headers.Add("X-Test-Ip", "10.0.20.2");
        var resp = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var jobs = doc.RootElement.GetProperty("user").GetProperty("jobs");
        Assert.Contains(jobs.EnumerateArray(), j => j.GetProperty("id").GetGuid() == roleId
            && j.GetProperty("name").GetString() == ChineseRoleName);
    }
}
