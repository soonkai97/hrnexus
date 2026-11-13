using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Phase 7m-a against REAL Postgres: everyone signed in can READ the company policy (every app needs the
/// working week), only policy.manage can change it, and a stale save is refused.
/// </summary>
[Collection("postgres")]
public class CompanyPolicyEndpointsTests(PostgresFixture fx) : IAsyncLifetime
{
    private TestApiFactory _factory = default!;

    public Task InitializeAsync() { _factory = new TestApiFactory(fx.ConnectionString); return Task.CompletedTask; }
    public Task DisposeAsync()    { _factory.Dispose(); return Task.CompletedTask; }

    private async Task<HttpClient> LoginAsync(string staffId, string password, string ip)
    {
        var client = _factory.CreateClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId, password, portal = (string?)null }),
        };
        login.Headers.Add("X-Test-Ip", ip);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);
        return client;
    }

    private static async Task<string> HireJobHolderAsync(HttpClient admin)
    {
        var jobName = $"POLJOB{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/staff/roles", new { name = jobName })).StatusCode);
        var roles = JsonDocument.Parse(await admin.GetStringAsync("/api/staff/roles")).RootElement;
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == jobName).GetProperty("id").GetGuid();
        var staffId = $"POL{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var create = await admin.PostAsJsonAsync("/api/staff", new
        {
            staffId, firstName = "Pol", lastName = "Icy", email = (string?)null, password = "Test@1234", roles = new[] { roleId },
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        return staffId;
    }

    private static object Body(int[] days, decimal hours = 8m, string mode = "capped", int? version = null) => new
    {
        workingDays = days, workdayHours = hours, annualLeaveAdvanceDays = 3, maxLeaveDays = 30,
        carryOverMode = mode, carryOverMaxHours = 14m, unpaidLeaveDivisor = 26m, version,
    };

    [Fact]
    public async Task EveryoneSignedInCanRead_OnlyPolicyManageCanChange()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.7.1");
        var staffId = await HireJobHolderAsync(admin);
        var employee = await LoginAsync(staffId, "Test@1234", "10.0.7.2");

        var read = await employee.GetAsync("/api/company-policy");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var policy = JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(5, policy.GetProperty("workingDays").GetArrayLength());       // Mon-Fri by default

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync("/api/company-policy", Body([1, 2, 3, 4, 5, 6]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/company-policy")).StatusCode);
    }

    [Fact]
    public async Task SavingBumpsTheVersion_AStaleSaveIsRefused_InvalidValuesAreRejected()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.7.3");
        var current = JsonDocument.Parse(await admin.GetStringAsync("/api/company-policy")).RootElement;
        var version = current.GetProperty("version").GetInt32();

        var saved = await admin.PutAsJsonAsync("/api/company-policy", Body([1, 2, 3, 4, 5, 6], version: version));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var newVersion = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement.GetProperty("version").GetInt32();
        Assert.Equal(version + 1, newVersion);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/company-policy", Body([1, 2, 3, 4, 5], version: version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/company-policy", Body([], version: newVersion))).StatusCode);

        // Put the shared test database back to the default week so other tests are unaffected.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/company-policy", Body([1, 2, 3, 4, 5], version: newVersion))).StatusCode);
    }
}
