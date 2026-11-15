using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRNexus.Domain.Entities;
using HRNexus.Infrastructure.Auth;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using P = HRNexus.Domain.Constants.AppConstants.Permissions;

namespace HRNexus.Integration.Tests;

/// <summary>
/// The setup that feeds a payroll run, over HTTP against a real PostgreSQL: recurring pay items, the monthly repayment of a
/// cash advance, and who reports to whom. Each test builds its own database.
///
/// WHY these tests matter: these three inputs change what people are paid, and the reporting line decides who may read and sign
/// off whose pay. Each must sit behind its own permission, two people editing the reporting line at once must not be able to create
/// a loop, and no amount may reach the audit trail or a plaintext column.
/// </summary>
[Collection("postgres")]
public class PayrollSetupEndpointsTests(PostgresFixture fx)
{
    private const string Password = "Test@1234";
    private static int _ip = 120;

    private sealed class Env(TestApiFactory factory, string database) : IDisposable
    {
        public TestApiFactory Factory { get; } = factory;
        public string Database { get; } = database;
        public Dictionary<string, Guid> Ids { get; } = [];
        public void Dispose()
        {
            Factory.Dispose();
            Npgsql.NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>PREP (view+manage), ADMIN (staff.manage), CASH (cashadvance.manage), one person per single permission, ALICE and BOB (paid), and an advance of RM500 for ALICE.</summary>
    private async Task<Env> NewEnvAsync()
    {
        var database = $"setup_{Guid.NewGuid():N}";
        await using (var db = fx.NewDbOn(database))
        {
            await db.Database.MigrateAsync();
            await SeedData.SeedDemoUsersAsync(db, new PasswordHasher());
            foreach (var row in await db.StatutoryRateTables.ToListAsync()) row.Verify("accountant", DateTime.UtcNow, "checked");
            await db.SaveChangesAsync();
        }
        var env = new Env(new TestApiFactory(fx.ConnectionString.Replace("Database=hrnexus_test", $"Database={database}")), database);
        await using var seed = fx.NewDbOn(database);
        var hash = new PasswordHasher().Hash(Password);

        async Task<Guid> RoleAsync(string name, params string[] keys)
        {
            var role = Role.Create(name);
            seed.Roles.Add(role);
            await seed.SaveChangesAsync();
            foreach (var k in keys) seed.RolePermissions.Add(RolePermission.Create(role.Id, k));
            await seed.SaveChangesAsync();
            return role.Id;
        }

        async Task<Staff> PersonAsync(string id, Guid? role = null, decimal? basic = null)
        {
            var s = Staff.Create(id, "First", id, null, hash, role is { } r ? [r] : [], dateOfBirth: new DateTime(1990, 3, 4));
            s.SetEmployment("citizen", new DateOnly(2024, 1, 1), null);
            seed.Staff.Add(s);
            if (basic is { } b) seed.SalaryProfiles.Add(SalaryProfile.Create(s.Id, new DateOnly(2020, 1, 1), b, "monthly", null, null, null));
            await seed.SaveChangesAsync();
            env.Ids[id] = s.Id;
            return s;
        }

        await PersonAsync("PREP", await RoleAsync("PREPARER", P.PayrollView, P.PayrollManage));
        await PersonAsync("ADMIN", await RoleAsync("STAFFADMIN", P.StaffManage, P.StaffView));
        await PersonAsync("CASH", await RoleAsync("CASHADMIN", P.CashAdvanceManage));
        var alice = await PersonAsync("ALICE", basic: 3000m);
        await PersonAsync("BOB", basic: 4000m);
        foreach (var key in new[] { P.PayrollView, P.PayrollManage, P.StaffManage, P.CashAdvanceManage })
            await PersonAsync("U_" + key.Replace(".", "_").ToUpperInvariant(), await RoleAsync("ONLY" + key.Replace(".", "").ToUpperInvariant(), key));
        var advance = CashAdvance.Create(alice.Id, 500m, new DateOnly(2026, 9, 2), "Rent", "admin");
        seed.CashAdvances.Add(advance);
        await seed.SaveChangesAsync();
        env.Ids["ADVANCE"] = advance.Id;
        return env;
    }

    private async Task<HttpClient> LoginAsync(Env env, string staffId)
    {
        var client = env.Factory.CreateClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { staffId, password = Password }) };
        login.Headers.Add("X-Test-Ip", $"10.0.41.{Interlocked.Increment(ref _ip) % 250 + 1}");
        var response = await client.SendAsync(login);
        Assert.True(response.IsSuccessStatusCode, $"login {staffId}: {response.StatusCode}");
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    // ── who may call what ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_setup_endpoint_refuses_a_caller_who_holds_only_some_other_permission()
    {
        using var env = await NewEnvAsync();
        var alice = env.Ids["ALICE"];
        var item = Guid.NewGuid();
        var someone = Guid.NewGuid();
        (HttpMethod Method, string Url, string Allowed, object? Body)[] endpoints =
        [
            (HttpMethod.Get, $"/api/staff/{alice}/pay-items", P.PayrollView, null),
            (HttpMethod.Post, $"/api/staff/{alice}/pay-items", P.PayrollManage, new { kind = "allowance", reason = "x", amount = 10m, countsForEpf = true, countsForSocsoEis = true, priority = 0, from = "2026-01-01" }),
            (HttpMethod.Put, $"/api/staff/{alice}/pay-items/{item}/end", P.PayrollManage, new { to = "2026-12-31" }),
            (HttpMethod.Delete, $"/api/staff/{alice}/pay-items/{item}", P.PayrollManage, null),
            (HttpMethod.Put, $"/api/staff/{someone}/reports-to", P.StaffManage, new { managerId = (Guid?)null }),
            (HttpMethod.Put, $"/api/cash-advances/{Guid.NewGuid()}/repayment", P.CashAdvanceManage, new { perMonth = 100m }),
        ];
        var keys = new[] { P.PayrollView, P.PayrollManage, P.StaffManage, P.CashAdvanceManage };
        var clients = new Dictionary<string, HttpClient>();
        foreach (var key in keys) clients[key] = await LoginAsync(env, "U_" + key.Replace(".", "_").ToUpperInvariant());
        var anonymous = env.Factory.CreateClient();

        foreach (var (method, url, allowed, body) in endpoints)
        {
            HttpRequestMessage Req() => new(method, url) { Content = body is null ? null : JsonContent.Create(body) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Req())).StatusCode);
            foreach (var key in keys)
            {
                var response = await clients[key].SendAsync(Req());
                var bareForbidden = response.StatusCode == HttpStatusCode.Forbidden && !(await response.Content.ReadAsStringAsync()).Contains("error");
                if (key == allowed) Assert.False(bareForbidden || response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {url} refused a holder of {key}: {response.StatusCode}");
                else Assert.True(bareForbidden, $"{method} {url} let a holder of only {key} through: {response.StatusCode}");
            }
        }
    }

    // ── pay items end to end ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_pay_item_added_over_http_reaches_the_next_run_is_listed_with_its_amount_and_is_stored_encrypted()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var alice = env.Ids["ALICE"];

        var add = await prep.PostAsJsonAsync($"/api/staff/{alice}/pay-items",
            new { kind = "allowance", reason = "Transport", amount = 150.75m, countsForEpf = true, countsForSocsoEis = true, priority = 0, from = "2026-01-01" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var itemId = (await JsonAsync(add)).GetProperty("id").GetGuid();

        var list = await JsonAsync(await prep.GetAsync($"/api/staff/{alice}/pay-items"));
        var row = Assert.Single(list.EnumerateArray().ToList());
        Assert.Equal((itemId, 150.75m, false), (row.GetProperty("id").GetGuid(), row.GetProperty("amount").GetDecimal(), row.GetProperty("inUse").GetBoolean()));

        var run = (await JsonAsync(await prep.PostAsJsonAsync("/api/payroll/runs", new { year = 2026, month = 10 }))).GetProperty("id").GetGuid();
        var detail = await JsonAsync(await prep.GetAsync($"/api/payroll/runs/{run}"));
        var aliceLine = detail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("staffId").GetString() == "ALICE");
        Assert.Equal(3150.75m, aliceLine.GetProperty("gross").GetDecimal());
        Assert.True((await JsonAsync(await prep.GetAsync($"/api/staff/{alice}/pay-items"))).EnumerateArray().Single().GetProperty("inUse").GetBoolean() == false);   // a draft has not frozen it

        await using var db = fx.NewDbOn(env.Database);
        var raw = await db.Database.SqlQueryRaw<string>("""SELECT "Amount" AS "Value" FROM "PayItems" """).SingleAsync();
        Assert.StartsWith("enc1:", raw);
        Assert.DoesNotContain("150", raw);

        // A bad item is a 400, an unknown person a 404, and a missing item a 404.
        Assert.Equal(HttpStatusCode.BadRequest, (await prep.PostAsJsonAsync($"/api/staff/{alice}/pay-items", new { kind = "allowance", reason = "x", amount = -1m, from = "2026-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await prep.PostAsJsonAsync($"/api/staff/{Guid.NewGuid()}/pay-items", new { kind = "allowance", reason = "x", amount = 10m, from = "2026-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await prep.DeleteAsync($"/api/staff/{alice}/pay-items/{Guid.NewGuid()}")).StatusCode);

        // It can be ended; and deleted while only a draft used it.
        Assert.Equal(HttpStatusCode.NoContent, (await prep.PutAsJsonAsync($"/api/staff/{alice}/pay-items/{itemId}/end", new { to = "2026-12-31" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await prep.DeleteAsync($"/api/staff/{alice}/pay-items/{itemId}")).StatusCode);
    }

    [Fact]
    public async Task Audit_rows_for_setup_changes_never_carry_an_amount_or_a_reason()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var admin = await LoginAsync(env, "ADMIN");
        var cash = await LoginAsync(env, "CASH");
        var alice = env.Ids["ALICE"];

        var item = (await JsonAsync(await prep.PostAsJsonAsync($"/api/staff/{alice}/pay-items",
            new { kind = "allowance", reason = "Secret bonus 4321", amount = 4321.50m, countsForEpf = true, countsForSocsoEis = true, priority = 0, from = "2026-01-01" }))).GetProperty("id").GetGuid();
        await prep.PutAsJsonAsync($"/api/staff/{alice}/pay-items/{item}/end", new { to = "2026-12-31" });
        await prep.DeleteAsync($"/api/staff/{alice}/pay-items/{item}");
        await admin.PutAsJsonAsync($"/api/staff/{alice}/reports-to", new { managerId = env.Ids["BOB"] });
        await cash.PutAsJsonAsync($"/api/cash-advances/{env.Ids["ADVANCE"]}/repayment", new { perMonth = 123.45m });

        await using var db = fx.NewDbOn(env.Database);
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == "PayItem" || a.EntityType == "CashAdvance" || a.Action == "ReportsToChanged").ToListAsync();
        Assert.True(rows.Count >= 5);
        Assert.All(rows, a => Assert.DoesNotMatch(new Regex(@"4321|123\.45|Secret"), a.Changes ?? ""));
        Assert.Equal("ALICE -> BOB", rows.Single(a => a.Action == "ReportsToChanged").Changes);
    }

    // ── the repayment of a cash advance ───────────────────────────────────────────

    [Fact]
    public async Task The_monthly_repayment_is_set_over_http_and_shows_in_the_list_with_what_is_left()
    {
        using var env = await NewEnvAsync();
        var cash = await LoginAsync(env, "CASH");

        Assert.Equal(HttpStatusCode.NoContent, (await cash.PutAsJsonAsync($"/api/cash-advances/{env.Ids["ADVANCE"]}/repayment", new { perMonth = 200m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cash.PutAsJsonAsync($"/api/cash-advances/{env.Ids["ADVANCE"]}/repayment", new { perMonth = 600m })).StatusCode);

        var row = (await JsonAsync(await cash.GetAsync("/api/cash-advances"))).EnumerateArray().Single();
        Assert.Equal((200m, 0m, 500m), (row.GetProperty("repaymentPerMonth").GetDecimal(), row.GetProperty("repaid").GetDecimal(), row.GetProperty("remaining").GetDecimal()));
        Assert.Equal(HttpStatusCode.NotFound, (await cash.PutAsJsonAsync($"/api/cash-advances/{Guid.NewGuid()}/repayment", new { perMonth = 100m })).StatusCode);
    }

    // ── who reports to whom ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_reporting_line_is_set_over_http_shows_in_the_staff_list_and_refuses_a_loop()
    {
        using var env = await NewEnvAsync();
        var admin = await LoginAsync(env, "ADMIN");
        var alice = env.Ids["ALICE"];
        var bob = env.Ids["BOB"];

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/staff/{alice}/reports-to", new { managerId = bob })).StatusCode);
        var list = await JsonAsync(await admin.GetAsync("/api/staff?pageSize=50"));
        var aliceRow = list.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("staffId").GetString() == "ALICE");
        Assert.Equal(bob, aliceRow.GetProperty("reportsToId").GetGuid());
        Assert.Equal("First BOB", aliceRow.GetProperty("reportsToName").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/staff/{bob}/reports-to", new { managerId = alice })).StatusCode);   // loop
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/staff/{alice}/reports-to", new { managerId = alice })).StatusCode);   // self
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/staff/{alice}/reports-to", new { managerId = (Guid?)null })).StatusCode);
    }

    /// <summary>
    /// WHY: A→B and B→A sent at the same moment would each pass a loop check made on the data before the other committed.
    /// Serializable makes one of them see the other's result, so exactly one succeeds and the line is never a loop.
    /// </summary>
    [Fact]
    public async Task Two_opposite_changes_at_the_same_moment_cannot_create_a_loop()
    {
        using var env = await NewEnvAsync();
        var first = await LoginAsync(env, "ADMIN");
        var second = await LoginAsync(env, "ADMIN");
        var alice = env.Ids["ALICE"];
        var bob = env.Ids["BOB"];

        var results = await Task.WhenAll(
            first.PutAsJsonAsync($"/api/staff/{alice}/reports-to", new { managerId = bob }),
            second.PutAsJsonAsync($"/api/staff/{bob}/reports-to", new { managerId = alice }));

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());   // exactly one wins
        await using var db = fx.NewDbOn(env.Database);
        var managers = await db.Staff.AsNoTracking().Where(s => s.Id == alice || s.Id == bob).Select(s => s.ReportsToId).ToListAsync();
        Assert.Equal(1, managers.Count(m => m != null));
    }
}
