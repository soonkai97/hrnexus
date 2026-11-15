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
/// The payroll run over HTTP against a real PostgreSQL, through the real pipeline (policies, exception middleware,
/// MediatR). Each test builds its own database so runs never collide.
///
/// WHY these tests matter: the commands are tested in isolation, but who may call them is decided in the pipeline.
/// Every endpoint must refuse a caller who holds some other payroll permission, a manager must see only their own
/// team, the acting person must always be the signed-in user, and no error or audit row may carry what anyone earns.
/// </summary>
[Collection("postgres")]
public class PayrollRunEndpointsTests(PostgresFixture fx)
{
    private const string Password = "Test@1234";
    private static int _ip = 40;

    private sealed class Env(TestApiFactory factory, string database) : IDisposable
    {
        public TestApiFactory Factory { get; } = factory;
        public string Database { get; } = database;
        public Dictionary<string, Guid> Ids { get; } = [];
        public void Dispose()
        {
            Factory.Dispose();
            // Each test has its own database and so its own connection pool; release them or the container runs out of connections.
            Npgsql.NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// A database with the (optionally verified) rates and these people: PREP (view+manage), HEAD (view+approve+lock+reopen),
    /// MGR (signoff, reports to nobody, paid), OTHERMGR (signoff, paid), ALICE (reports to MGR), BOB (reports to OTHERMGR),
    /// and one person per single permission (U_VIEW, U_MANAGE, ...) for the permission matrix.
    /// </summary>
    private async Task<Env> NewEnvAsync(bool verifyRates = true)
    {
        var database = $"runapi_{Guid.NewGuid():N}";
        await using (var db = fx.NewDbOn(database))
        {
            await db.Database.MigrateAsync();
            await SeedData.SeedDemoUsersAsync(db, new PasswordHasher());
            if (verifyRates)
                foreach (var row in await db.StatutoryRateTables.ToListAsync())
                    row.Verify("accountant", DateTime.UtcNow, "checked");
            await db.SaveChangesAsync();
        }
        var factory = new TestApiFactory(fx.ConnectionString.Replace("Database=hrnexus_test", $"Database={database}"));
        var env = new Env(factory, database);
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

        async Task<Staff> PersonAsync(string id, Guid? role = null, decimal? basic = null, Guid? reportsTo = null)
        {
            var s = Staff.Create(id, "First", id, null, hash, role is { } r ? [r] : [], dateOfBirth: new DateTime(1990, 3, 4));
            s.SetEmployment("citizen", new DateOnly(2024, 1, 1), null);
            s.SetReportsTo(reportsTo);
            seed.Staff.Add(s);
            if (basic is { } b) seed.SalaryProfiles.Add(SalaryProfile.Create(s.Id, new DateOnly(2020, 1, 1), b, "monthly", null, null, null));
            await seed.SaveChangesAsync();
            env.Ids[id] = s.Id;
            return s;
        }

        await PersonAsync("PREP", await RoleAsync("PREPARER", P.PayrollView, P.PayrollManage));
        await PersonAsync("HEAD", await RoleAsync("HEADHR", P.PayrollView, P.PayrollApprove, P.PayrollLock, P.PayrollReopen));
        var signoffRole = await RoleAsync("MANAGERS", P.PayrollSignoff);
        var mgr = await PersonAsync("MGR", signoffRole, 5000m);
        var other = await PersonAsync("OTHERMGR", signoffRole, 5000m);
        await PersonAsync("ALICE", basic: 3000m, reportsTo: mgr.Id);
        await PersonAsync("BOB", basic: 4000m, reportsTo: other.Id);
        foreach (var key in new[] { P.PayrollView, P.PayrollManage, P.PayrollApprove, P.PayrollSignoff, P.PayrollLock, P.PayrollReopen })
            await PersonAsync("U_" + key.Replace("payroll.", "").ToUpperInvariant(), await RoleAsync("ONLY" + key.Replace("payroll.", "").ToUpperInvariant(), key));
        return env;
    }

    private async Task<HttpClient> LoginAsync(Env env, string staffId)
    {
        var client = env.Factory.CreateClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { staffId, password = Password }) };
        login.Headers.Add("X-Test-Ip", $"10.0.40.{Interlocked.Increment(ref _ip) % 250 + 1}");
        var response = await client.SendAsync(login);
        Assert.True(response.IsSuccessStatusCode, $"login {staffId}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<long> VersionOfAsync(HttpClient viewer, Guid runId) =>
        (await JsonAsync(await viewer.GetAsync($"/api/payroll/runs/{runId}"))).GetProperty("summary").GetProperty("version").GetInt64();

    private static async Task<Guid> StartAsync(HttpClient prep, int month = 10)
    {
        var r = await prep.PostAsJsonAsync("/api/payroll/runs", new { year = 2026, month });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await JsonAsync(r)).GetProperty("id").GetGuid();
    }

    // ── who may call what ─────────────────────────────────────────────────────────

    /// <summary>
    /// Every endpoint, tried by someone holding ONLY each one of the six payroll permissions in turn, and by nobody
    /// signed in: refused with 403 (401 when anonymous) unless the person holds a permission the endpoint accepts.
    /// </summary>
    [Fact]
    public async Task Every_endpoint_refuses_a_caller_who_holds_only_some_other_payroll_permission()
    {
        using var env = await NewEnvAsync();
        var run = Guid.NewGuid();
        var someone = Guid.NewGuid();
        var body = new { expectedVersion = 0L, year = 2026, month = 10, reason = "x", pcb = 1m, excluded = false };
        (HttpMethod Method, string Url, string[] Allowed)[] endpoints =
        [
            (HttpMethod.Get, "/api/payroll/runs", [P.PayrollView]),
            (HttpMethod.Get, $"/api/payroll/runs/{run}", [P.PayrollView]),
            (HttpMethod.Get, "/api/payroll/runs/team", [P.PayrollSignoff]),
            (HttpMethod.Get, $"/api/payroll/runs/{run}/team", [P.PayrollSignoff]),
            (HttpMethod.Post, "/api/payroll/runs", [P.PayrollManage]),
            (HttpMethod.Delete, $"/api/payroll/runs/{run}", [P.PayrollManage]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/recalculate", [P.PayrollManage]),
            (HttpMethod.Put, $"/api/payroll/runs/{run}/lines/{someone}", [P.PayrollManage]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/submit", [P.PayrollManage]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/lines/{someone}/signoff", [P.PayrollSignoff, P.PayrollApprove]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/signoff-all", [P.PayrollApprove]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/return", [P.PayrollApprove]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/approve", [P.PayrollApprove]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/lock", [P.PayrollLock]),
            (HttpMethod.Post, $"/api/payroll/runs/{run}/reopen", [P.PayrollReopen]),
        ];
        var keys = new[] { P.PayrollView, P.PayrollManage, P.PayrollApprove, P.PayrollSignoff, P.PayrollLock, P.PayrollReopen };
        var anonymous = env.Factory.CreateClient();
        var clients = new Dictionary<string, HttpClient>();
        foreach (var key in keys) clients[key] = await LoginAsync(env, "U_" + key.Replace("payroll.", "").ToUpperInvariant());

        foreach (var (method, url, allowed) in endpoints)
        {
            HttpRequestMessage Req() => new(method, url) { Content = method == HttpMethod.Get || method == HttpMethod.Delete ? null : JsonContent.Create(body) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Req())).StatusCode);
            foreach (var key in keys)
            {
                var response = await clients[key].SendAsync(Req());
                var status = response.StatusCode;
                if (allowed.Contains(key))
                {
                    // A 403 with a JSON error is the command itself refusing (for example a manager signing someone who does not
                    // report to them); a bare 403 is the policy, which must not happen for a permission the endpoint accepts.
                    var refusedByPolicy = status == HttpStatusCode.Forbidden && !(await response.Content.ReadAsStringAsync()).Contains("error");
                    Assert.True(status != HttpStatusCode.Unauthorized && !refusedByPolicy, $"{method} {url} refused a holder of {key}: {status}");
                }
                else
                    Assert.True(status == HttpStatusCode.Forbidden, $"{method} {url} let a holder of only {key} through: {status}");
            }
        }
    }

    // ── the whole lifecycle ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_month_goes_from_start_to_locked_and_back_over_http_with_each_person_doing_only_their_part()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var head = await LoginAsync(env, "HEAD");
        var mgr = await LoginAsync(env, "MGR");
        var id = await StartAsync(prep);

        var detail = await JsonAsync(await prep.GetAsync($"/api/payroll/runs/{id}"));
        Assert.Equal(["ALICE", "BOB", "MGR", "OTHERMGR"], detail.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("staffId").GetString()).Order());
        Assert.Equal("draft", detail.GetProperty("summary").GetProperty("state").GetString());

        var version = await VersionOfAsync(prep, id);
        var edit = await prep.PutAsJsonAsync($"/api/payroll/runs/{id}/lines/{env.Ids["ALICE"]}", new { pcb = 100m, excluded = false, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var alice = (await JsonAsync(await prep.GetAsync($"/api/payroll/runs/{id}"))).GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("staffId").GetString() == "ALICE");
        Assert.Equal(100m, alice.GetProperty("pcb").GetDecimal());

        Assert.Equal(HttpStatusCode.OK, (await prep.PostAsJsonAsync($"/api/payroll/runs/{id}/submit", new { expectedVersion = await VersionOfAsync(prep, id) })).StatusCode);

        // The manager sees only their own report, and can sign it.
        var team = await JsonAsync(await mgr.GetAsync($"/api/payroll/runs/{id}/team"));
        Assert.Equal("ALICE", Assert.Single(team.GetProperty("lines").EnumerateArray().ToList()).GetProperty("staffId").GetString());
        Assert.Equal(HttpStatusCode.OK, (await mgr.PostAsJsonAsync($"/api/payroll/runs/{id}/lines/{env.Ids["ALICE"]}/signoff", new { expectedVersion = await VersionOfAsync(head, id) })).StatusCode);

        // The preparer cannot approve; the head of HR signs the rest and approves.
        Assert.Equal(HttpStatusCode.Forbidden, (await prep.PostAsJsonAsync($"/api/payroll/runs/{id}/approve", new { expectedVersion = await VersionOfAsync(prep, id) })).StatusCode);
        var all = await head.PostAsJsonAsync($"/api/payroll/runs/{id}/signoff-all", new { expectedVersion = await VersionOfAsync(head, id) });
        Assert.Equal(3, (await JsonAsync(all)).GetProperty("signed").GetInt32());   // BOB, MGR, OTHERMGR
        Assert.Equal(HttpStatusCode.OK, (await head.PostAsJsonAsync($"/api/payroll/runs/{id}/approve", new { expectedVersion = await VersionOfAsync(head, id) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await head.PostAsJsonAsync($"/api/payroll/runs/{id}/lock", new { expectedVersion = await VersionOfAsync(head, id) })).StatusCode);

        var list = await JsonAsync(await head.GetAsync("/api/payroll/runs"));
        var summary = Assert.Single(list.EnumerateArray().ToList());
        Assert.Equal("locked", summary.GetProperty("state").GetString());
        Assert.Equal(4, summary.GetProperty("people").GetInt32());
        Assert.Equal(3000m + 4000m + 5000m + 5000m, summary.GetProperty("totalGross").GetDecimal());

        Assert.Equal(HttpStatusCode.NoContent, (await head.PostAsJsonAsync($"/api/payroll/runs/{id}/reopen", new { expectedVersion = await VersionOfAsync(head, id) })).StatusCode);
        Assert.Equal("draft", (await JsonAsync(await head.GetAsync($"/api/payroll/runs/{id}"))).GetProperty("summary").GetProperty("state").GetString());
    }

    // ── a manager sees only their own team ────────────────────────────────────────

    /// <summary>WHY: a manager holds only payroll.signoff. They must see their reports and nobody else, and may not list or open runs.</summary>
    [Fact]
    public async Task A_manager_sees_only_their_team_and_cannot_sign_or_read_anyone_elses_pay()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var mgr = await LoginAsync(env, "MGR");
        var other = await LoginAsync(env, "OTHERMGR");
        var id = await StartAsync(prep);
        await prep.PostAsJsonAsync($"/api/payroll/runs/{id}/submit", new { expectedVersion = await VersionOfAsync(prep, id) });

        var waiting = await JsonAsync(await mgr.GetAsync("/api/payroll/runs/team"));
        var waitingRun = Assert.Single(waiting.EnumerateArray().ToList());
        Assert.Equal((id, 1, 1), (waitingRun.GetProperty("runId").GetGuid(), waitingRun.GetProperty("lines").GetInt32(), waitingRun.GetProperty("toSign").GetInt32()));

        var mine = await JsonAsync(await mgr.GetAsync($"/api/payroll/runs/{id}/team"));
        var theirs = await JsonAsync(await other.GetAsync($"/api/payroll/runs/{id}/team"));

        Assert.Equal("ALICE", Assert.Single(mine.GetProperty("lines").EnumerateArray().ToList()).GetProperty("staffId").GetString());
        Assert.Equal("BOB", Assert.Single(theirs.GetProperty("lines").EnumerateArray().ToList()).GetProperty("staffId").GetString());
        Assert.DoesNotContain("BOB", await mgr.GetStringAsync($"/api/payroll/runs/{id}/team"));
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.GetAsync("/api/payroll/runs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.GetAsync($"/api/payroll/runs/{id}")).StatusCode);
        var version = await VersionOfAsync(prep, id);
        var notMine = await mgr.PostAsJsonAsync($"/api/payroll/runs/{id}/lines/{env.Ids["BOB"]}/signoff", new { expectedVersion = version });
        Assert.Equal(HttpStatusCode.Forbidden, notMine.StatusCode);
    }

    // ── the acting person is always the signed-in user ────────────────────────────

    [Fact]
    public async Task The_history_and_the_audit_row_name_the_signed_in_person_whatever_the_body_says()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var r = await prep.PostAsJsonAsync("/api/payroll/runs", new { year = 2026, month = 10, by = env.Ids["HEAD"], staffId = env.Ids["HEAD"], performedById = env.Ids["HEAD"] });
        var id = (await JsonAsync(r)).GetProperty("id").GetGuid();

        await using var db = fx.NewDbOn(env.Database);
        var run = await db.PayrollRuns.AsNoTracking().Include(x => x.Actors).SingleAsync(x => x.Id == id);
        Assert.Equal(env.Ids["PREP"], run.Actors.Single(a => a.Action == "started").StaffId);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "PayrollRun" && a.Action == "Started");
        Assert.Equal(env.Ids["PREP"], audit.PerformedById);
    }

    // ── no amounts in errors or audit rows ────────────────────────────────────────

    [Fact]
    public async Task Refusals_and_audit_rows_never_carry_what_anyone_earns()
    {
        using var env = await NewEnvAsync(verifyRates: false);
        var prep = await LoginAsync(env, "PREP");
        var head = await LoginAsync(env, "HEAD");
        var mgr = await LoginAsync(env, "MGR");
        var id = await StartAsync(prep);
        var stale = await prep.PostAsJsonAsync($"/api/payroll/runs/{id}/submit", new { expectedVersion = 1L });                // stale version
        var started = await prep.PostAsJsonAsync("/api/payroll/runs", new { year = 2026, month = 10 });                          // second run for the month
        await prep.PostAsJsonAsync($"/api/payroll/runs/{id}/submit", new { expectedVersion = await VersionOfAsync(prep, id) });
        var earlySign = await mgr.PostAsJsonAsync($"/api/payroll/runs/{id}/lines/{env.Ids["BOB"]}/signoff", new { expectedVersion = await VersionOfAsync(head, id) });   // not their report
        await head.PostAsJsonAsync($"/api/payroll/runs/{id}/signoff-all", new { expectedVersion = await VersionOfAsync(head, id) });
        var unverified = await head.PostAsJsonAsync($"/api/payroll/runs/{id}/approve", new { expectedVersion = await VersionOfAsync(head, id) });   // rates not verified

        // Every figure the run holds (gross, net, employer cost, each payslip line, the totals), not just the basic salaries.
        var detail = await JsonAsync(await head.GetAsync($"/api/payroll/runs/{id}"));
        var figures = new HashSet<decimal> { detail.GetProperty("summary").GetProperty("totalGross").GetDecimal(), detail.GetProperty("summary").GetProperty("totalNet").GetDecimal() };
        foreach (var line in detail.GetProperty("lines").EnumerateArray())
        {
            foreach (var name in new[] { "gross", "net", "employerCost" }) figures.Add(line.GetProperty(name).GetDecimal());
            foreach (var item in line.GetProperty("items").EnumerateArray()) figures.Add(item.GetProperty("amount").GetDecimal());
        }
        var wholeFigures = figures.Where(f => f >= 100m).Select(f => ((long)f).ToString()).Distinct().ToList();
        Assert.True(wholeFigures.Count >= 8, "the run should hold plenty of figures to look for");
        foreach (var response in new[] { stale, started, earlySign, unverified })
        {
            var text = await response.Content.ReadAsStringAsync();
            foreach (var figure in wholeFigures)
                Assert.False(Regex.IsMatch(text, $@"\b{figure}(\.\d+)?\b"), $"the figure {figure} leaked into an error body: {text}");
        }
        Assert.Contains("EPF (citizen)", await unverified.Content.ReadAsStringAsync());

        await using var db = fx.NewDbOn(env.Database);
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == "PayrollRun").ToListAsync();
        Assert.NotEmpty(rows);
        // Only the month, and at most a staff code or a line count: never an amount, a reason or a name.
        Assert.All(rows, a => Assert.Matches(@"^\d{4}-\d{2}( [A-Z][A-Z0-9_]*| \d+ lines)?$", a.Changes ?? ""));
    }

    /// <summary>
    /// WHY: every figure a run stores is pay. A raw read of the database (a backup, a stray query) must see ciphertext,
    /// never "3000.00".
    /// </summary>
    [Fact]
    public async Task What_the_run_stores_after_a_start_over_http_is_ciphertext_in_the_database()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        await StartAsync(prep);

        await using var db = fx.NewDbOn(env.Database);
        foreach (var (table, column) in new[] { ("PayrollRunLines", "Gross"), ("PayrollRunLines", "Net"), ("PayrollRunLineItems", "Amount"), ("PayrollRunWarnings", "Message") })
        {
            var sql = $"""SELECT "{column}" AS "Value" FROM "{table}" ORDER BY "Id" LIMIT 1""";   // names come from the list above, not from input
            var raw = await db.Database.SqlQueryRaw<string>(sql).SingleAsync();
            Assert.StartsWith("enc1:", raw);
        }
    }

    /// <summary>
    /// WHY: the API's policy cache can lag a few minutes behind a deactivation. A reader whose access was just withdrawn
    /// must be refused on their very next request, even though their cookie is still valid.
    /// </summary>
    [Fact]
    public async Task Someone_deactivated_while_signed_in_loses_access_to_payroll_on_their_next_request()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var viewer = await LoginAsync(env, "U_VIEW");
        var id = await StartAsync(prep);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/payroll/runs/{id}")).StatusCode);   // warms the permission cache

        await using (var db = fx.NewDbOn(env.Database))
        {
            (await db.Staff.SingleAsync(s => s.Id == env.Ids["U_VIEW"])).Deactivate();
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/payroll/runs/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/payroll/runs")).StatusCode);
    }

    [Fact]
    public async Task A_sign_off_for_someone_unknown_is_refused_the_same_way_for_a_manager_and_a_404_for_the_head_of_hr()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var mgr = await LoginAsync(env, "MGR");
        var head = await LoginAsync(env, "HEAD");
        var id = await StartAsync(prep);
        await prep.PostAsJsonAsync($"/api/payroll/runs/{id}/submit", new { expectedVersion = await VersionOfAsync(prep, id) });
        var version = await VersionOfAsync(head, id);

        var onPayroll = await mgr.PostAsJsonAsync($"/api/payroll/runs/{id}/lines/{env.Ids["BOB"]}/signoff", new { expectedVersion = version });
        var unknown = await mgr.PostAsJsonAsync($"/api/payroll/runs/{id}/lines/{Guid.NewGuid()}/signoff", new { expectedVersion = version });
        var unknownToHead = await head.PostAsJsonAsync($"/api/payroll/runs/{id}/lines/{Guid.NewGuid()}/signoff", new { expectedVersion = version });

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (onPayroll.StatusCode, unknown.StatusCode));
        Assert.Equal(await onPayroll.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknownToHead.StatusCode);
    }

    [Fact]
    public async Task A_run_that_does_not_exist_is_a_404_and_a_changed_run_is_a_409()
    {
        using var env = await NewEnvAsync();
        var prep = await LoginAsync(env, "PREP");
        var id = await StartAsync(prep);

        Assert.Equal(HttpStatusCode.NotFound, (await prep.GetAsync($"/api/payroll/runs/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await prep.PostAsJsonAsync($"/api/payroll/runs/{id}/recalculate", new { expectedVersion = 0L })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await prep.PostAsJsonAsync("/api/payroll/runs", new { year = 2026, month = 13 })).StatusCode);
    }
}
