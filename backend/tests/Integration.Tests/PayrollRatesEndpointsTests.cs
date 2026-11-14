using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Phase 8b over HTTP against real Postgres: who may read the statutory rates, who may verify them, and that the
/// verification leaves a figure-free audit row. Each test inserts its OWN rate row (unique effective date) because
/// verification cannot be undone and the database is shared between tests.
///
/// WHY these tests matter: the separation of duties is the whole point of the new permission. The person who
/// prepares payroll (payroll.manage) must not be able to sign off the rates that gate their own approval.
/// </summary>
[Collection("postgres")]
public class PayrollRatesEndpointsTests(PostgresFixture fx) : IAsyncLifetime
{
    private TestApiFactory _factory = default!;
    private int _ip = 20;

    public Task InitializeAsync() { _factory = new TestApiFactory(fx.ConnectionString); return Task.CompletedTask; }
    public Task DisposeAsync()    { _factory.Dispose(); return Task.CompletedTask; }

    private async Task<HttpClient> LoginAsync(string staffId, string password)
    {
        var client = _factory.CreateClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId, password }),
        };
        login.Headers.Add("X-Test-Ip", $"10.0.20.{Interlocked.Increment(ref _ip)}");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);
        return client;
    }

    /// <summary>Hires someone whose role holds exactly the given permissions and signs them in.</summary>
    private async Task<(HttpClient Client, string StaffId)> PersonWithAsync(HttpClient admin, params string[] permissions)
    {
        var roleName = $"RATE{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/staff/roles", new { name = roleName })).StatusCode);
        if (permissions.Length > 0)
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/permissions/roles/{roleName}", new { permissions })).StatusCode);
        var roleId = JsonDocument.Parse(await admin.GetStringAsync("/api/staff/roles")).RootElement.EnumerateArray()
            .First(r => r.GetProperty("name").GetString() == roleName).GetProperty("id").GetGuid();
        var staffId = $"RT{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var create = await admin.PostAsJsonAsync("/api/staff", new
        {
            staffId, firstName = "Rate", lastName = staffId, email = (string?)null, password = "Test@1234", roles = new[] { roleId },
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        return (await LoginAsync(staffId, "Test@1234"), staffId);
    }

    /// <summary>A fresh, unverified rate row of its own (a date no other test uses).</summary>
    private async Task<Guid> OwnRowAsync(string scheme = StatutoryScheme.HrdOptional, bool active = false)
    {
        await using var db = fx.NewDb();
        var date = new DateOnly(2030, 1, 1).AddDays(Random.Shared.Next(1, 3000));
        var row = StatutoryRateTable.CreatePercentage(scheme, "citizen", date, 0m, 0.005m, RoundingRule.NearestCent, "test row", active: active);
        db.StatutoryRateTables.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    [Fact]
    public async Task Rates_are_hidden_from_someone_without_payroll_view_and_listed_for_someone_with_it()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234");
        var (nobody, _) = await PersonWithAsync(admin);
        var (viewer, _) = await PersonWithAsync(admin, "payroll.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/payroll/rates")).StatusCode);

        var list = JsonDocument.Parse(await viewer.GetStringAsync("/api/payroll/rates")).RootElement;
        Assert.True(list.GetArrayLength() >= 12);
        var epf = list.EnumerateArray().First(r => r.GetProperty("scheme").GetString() == "epf" && r.GetProperty("citizenshipClass").GetString() == "citizen"
            && r.GetProperty("effectiveFrom").GetString() == "2020-01-01");   // the seeded row (other tests add their own)
        Assert.Equal(0.11m, epf.GetProperty("employeeRate").GetDecimal());
        Assert.Equal("up-to-next-ringgit", epf.GetProperty("rounding").GetString());
        Assert.Equal("whole-wage-by-threshold", epf.GetProperty("employerRateMode").GetString());
    }

    [Fact]
    public async Task The_person_who_prepares_payroll_cannot_verify_the_rates_only_the_verifier_can()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234");
        var (preparer, _) = await PersonWithAsync(admin, "payroll.view", "payroll.manage");
        var (verifier, verifierStaffId) = await PersonWithAsync(admin, "payroll.view", "payroll.rates.verify");
        var id = await OwnRowAsync();

        var refused = await preparer.PostAsJsonAsync($"/api/payroll/rates/{id}/verify", new { note = "I checked my own work" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var ok = await verifier.PostAsJsonAsync($"/api/payroll/rates/{id}/verify", new { note = "Checked against the official table" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        await using var db = fx.NewDb();
        var row = await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.True(row.Verified);
        Assert.Equal(verifierStaffId, row.VerifiedBy);   // the signed-in user, not anything from the request
        Assert.Equal("Checked against the official table", row.VerificationNote);
    }

    [Fact]
    public async Task Verifying_writes_an_audit_row_that_names_the_rate_but_has_no_figures()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234");
        var (verifier, _) = await PersonWithAsync(admin, "payroll.view", "payroll.rates.verify");
        var id = await OwnRowAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await verifier.PostAsJsonAsync($"/api/payroll/rates/{id}/verify", new { note = "ok" })).StatusCode);

        await using var db = fx.NewDb();
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "StatutoryRate" && a.EntityId == id.ToString());
        Assert.Equal("Verified", audit.Action);
        Assert.Contains("hrd-optional", audit.Changes);
        Assert.DoesNotContain("0.005", audit.Changes);
    }

    [Fact]
    public async Task A_second_verification_is_a_conflict_and_a_missing_note_or_row_is_refused_cleanly()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234");
        var (verifier, _) = await PersonWithAsync(admin, "payroll.rates.verify");
        var id = await OwnRowAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await verifier.PostAsJsonAsync($"/api/payroll/rates/{id}/verify", new { note = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await verifier.PostAsJsonAsync($"/api/payroll/rates/{Guid.NewGuid()}/verify", new { note = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await verifier.PostAsJsonAsync($"/api/payroll/rates/{id}/verify", new { note = "first" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await verifier.PostAsJsonAsync($"/api/payroll/rates/{id}/verify", new { note = "again" })).StatusCode);
    }

    [Fact]
    public async Task The_active_switch_needs_the_verify_permission_and_will_not_double_charge_the_levy()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234");
        var (preparer, _) = await PersonWithAsync(admin, "payroll.manage");
        var (verifier, _) = await PersonWithAsync(admin, "payroll.rates.verify");
        var optionalId = await OwnRowAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsJsonAsync($"/api/payroll/rates/{optionalId}/active", new { active = true })).StatusCode);

        // The seeded compulsory 1% row is already on, so switching an optional row on would charge the levy twice.
        Assert.Equal(HttpStatusCode.Conflict, (await verifier.PostAsJsonAsync($"/api/payroll/rates/{optionalId}/active", new { active = true })).StatusCode);

        // And only the HRD rows have a switch at all.
        var epfId = await OwnRowAsync(StatutoryScheme.Epf, active: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await verifier.PostAsJsonAsync($"/api/payroll/rates/{epfId}/active", new { active = false })).StatusCode);
    }

    [Fact]
    public async Task Nothing_about_a_rate_can_be_edited_through_the_API()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234");
        var id = await OwnRowAsync();

        // There is no edit route: a correction is a new row.
        var put = (await admin.PutAsJsonAsync($"/api/payroll/rates/{id}", new { employerRate = 0.5m })).StatusCode;
        var del = (await admin.DeleteAsync($"/api/payroll/rates/{id}")).StatusCode;
        Assert.All(new[] { put, del }, c => Assert.True(c is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"expected no such route, got {c}"));

        await using var db = fx.NewDb();
        Assert.Equal(0.005m, (await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == id)).EmployerRate);
    }
}
