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

    private static async Task<JsonElement?> FindRoleAsync(HttpClient client, string name)
    {
        var resp = await client.GetAsync("/api/staff/roles");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        foreach (var r in doc.RootElement.EnumerateArray())
            if (string.Equals(r.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)) return r.Clone();
        return null;
    }

    [Fact]
    public async Task CreateRole_PersistsAndReadsBack_WithoutAnyPayFields()
    {
        var client = await AdminClientAsync("10.0.9.5");
        var name = $"JOB{Guid.NewGuid():N}"[..12];

        var create = await client.PostAsJsonAsync("/api/staff/roles", new { name });
        Assert.Equal(HttpStatusCode.NoContent, create.StatusCode);

        var role = await FindRoleAsync(client, name);
        Assert.NotNull(role);
        Assert.False(role!.Value.TryGetProperty("hourlyRate", out _), "hourly pay is gone from the product");
        Assert.False(role.Value.TryGetProperty("payType", out _));
    }

    [Fact]
    public async Task RenameRole_ChangesTheName_AndRefusesSystemRolesAndDuplicates()
    {
        var client = await AdminClientAsync("10.0.9.6");
        var name  = $"OLD{Guid.NewGuid():N}"[..12];
        var other = $"OTH{Guid.NewGuid():N}"[..12];
        await client.PostAsJsonAsync("/api/staff/roles", new { name });
        await client.PostAsJsonAsync("/api/staff/roles", new { name = other });
        var id = (await FindRoleAsync(client, name))!.Value.GetProperty("id").GetGuid();

        var renamed = $"NEW{Guid.NewGuid():N}"[..12];
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/staff/roles/{id}", new { name = renamed })).StatusCode);
        Assert.NotNull(await FindRoleAsync(client, renamed));

        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"/api/staff/roles/{id}", new { name = other })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/staff/roles/{id}", new { name = " " })).StatusCode);

        // The first administrator job may be renamed (nothing looks it up by name), and renamed back.
        var system = (await FindRoleAsync(client, "ADMINISTRATOR"))!.Value;
        Assert.True(system.GetProperty("isSystem").GetBoolean());
        var systemId = system.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/staff/roles/{systemId}", new { name = "CHIEF ADMINISTRATOR" })).StatusCode);
        Assert.NotNull(await FindRoleAsync(client, "CHIEF ADMINISTRATOR"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/staff/roles/{systemId}", new { name = "ADMINISTRATOR" })).StatusCode);

        // But its permissions cannot be edited (it always holds every permission) and it cannot be deleted.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/permissions/roles/ADMINISTRATOR", new { permissions = new[] { "staff.view" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/staff/roles/{systemId}")).StatusCode);
    }
}
