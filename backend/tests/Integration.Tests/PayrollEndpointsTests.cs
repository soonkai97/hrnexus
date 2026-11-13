using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Drives the role pay-type HTTP endpoints end-to-end against REAL Postgres — the layer the
/// handler unit tests (EF InMemory) never reach: authenticated request → model binding → Npgsql → JSON.
/// </summary>
[Collection("postgres")]
public class PayrollEndpointsTests(PostgresFixture fx) : IAsyncLifetime
{
    private TestApiFactory _factory = default!;

    public Task InitializeAsync() { _factory = new TestApiFactory(fx.ConnectionString); return Task.CompletedTask; }
    public Task DisposeAsync()    { _factory.Dispose(); return Task.CompletedTask; }

    /// <summary>Logs in as the seeded HRADMIN (all permissions) and returns a cookie-bearing client.</summary>
    private async Task<HttpClient> AdminClientAsync(string ip)
    {
        var client = _factory.CreateClient();   // Mvc.Testing client auto-persists Set-Cookie
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId = "HRADMIN", password = "Admin@1234", portal = (string?)null }),
        };
        login.Headers.Add("X-Test-Ip", ip);
        var resp = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return client;
    }

    [Fact]
    public async Task CreateRole_WithFixedPayType_PersistsAndReadsBack()
    {
        var client = await AdminClientAsync("10.0.9.5");
        var name = $"PKG{Guid.NewGuid():N}"[..12];

        var create = await client.PostAsJsonAsync("/api/staff/roles",
            new { name, hourlyRate = 500m, payType = "fixed" });
        Assert.Equal(HttpStatusCode.NoContent, create.StatusCode);

        var rolesResp = await client.GetAsync("/api/staff/roles");
        Assert.Equal(HttpStatusCode.OK, rolesResp.StatusCode);

        using var doc = JsonDocument.Parse(await rolesResp.Content.ReadAsStringAsync());
        var role = doc.RootElement.EnumerateArray()
            .FirstOrDefault(r => string.Equals(r.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Object, role.ValueKind);   // found
        Assert.Equal("fixed", role.GetProperty("payType").GetString());
    }
}
