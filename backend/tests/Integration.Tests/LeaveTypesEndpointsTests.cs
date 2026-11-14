using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave.Commands;
using HRNexus.Domain.Leave;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Phase 7q against REAL Postgres: the leave-type catalogue is readable by everyone signed in but only
/// editable with policy.manage, a stale save is refused, two administrators saving at once cannot both win,
/// and the catalogue really drives what an employee can apply for (no-show is admin-only; a 98-day maternity
/// is accepted and stored as 784 hours).
/// Every test leaves the catalogue as it found it (the built-in types, no extras) because the database is shared.
/// </summary>
[Collection("postgres")]
public class LeaveTypesEndpointsTests(PostgresFixture fx) : IAsyncLifetime
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

    private static async Task<string> HireEmployeeAsync(HttpClient admin)
    {
        var jobName = $"LTJOB{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/staff/roles", new { name = jobName })).StatusCode);
        var roles = JsonDocument.Parse(await admin.GetStringAsync("/api/staff/roles")).RootElement;
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == jobName).GetProperty("id").GetGuid();
        var staffId = $"LT{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var create = await admin.PostAsJsonAsync("/api/staff", new
        {
            staffId, firstName = "Leave", lastName = "Types", email = (string?)null, password = "Test@1234", roles = new[] { roleId },
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        return staffId;
    }

    private static JsonElement[] Rows(JsonElement catalogue) => catalogue.GetProperty("types").EnumerateArray().ToArray();

    /// <summary>The catalogue as the settings page would send it back: every type, optionally with changes.</summary>
    private static object[] Body(JsonElement[] rows, params object[] extra) =>
        [.. rows.Select(r => (object)new
        {
            key = r.GetProperty("key").GetString(), label = r.GetProperty("label").GetString(), colour = r.GetProperty("colour").GetString(),
            sortOrder = r.GetProperty("sortOrder").GetInt32(), active = r.GetProperty("active").GetBoolean(), pool = r.GetProperty("pool").GetString(),
            allowanceDays = r.GetProperty("allowanceDays").ValueKind == JsonValueKind.Null ? (decimal?)null : r.GetProperty("allowanceDays").GetDecimal(),
            maxDaysPerRequest = r.GetProperty("maxDaysPerRequest").ValueKind == JsonValueKind.Null ? (decimal?)null : r.GetProperty("maxDaysPerRequest").GetDecimal(),
            autoApprove = r.GetProperty("autoApprove").GetBoolean(),
            advanceNoticeDays = r.GetProperty("advanceNoticeDays").ValueKind == JsonValueKind.Null ? (int?)null : r.GetProperty("advanceNoticeDays").GetInt32(),
            advanceNoticeFromPolicy = r.GetProperty("advanceNoticeFromPolicy").GetBoolean(), deductsPay = r.GetProperty("deductsPay").GetBoolean(),
            adminOnly = r.GetProperty("adminOnly").GetBoolean(), countsCalendarDays = r.GetProperty("countsCalendarDays").GetBoolean(),
            sensitive = r.GetProperty("sensitive").GetBoolean(),
        }), .. extra];

    private static object Study() => new
    {
        key = "study", label = "Study", colour = "info", sortOrder = 100, active = true, pool = "none",
        allowanceDays = (decimal?)null, maxDaysPerRequest = (decimal?)5m, autoApprove = false, advanceNoticeDays = (int?)null,
        advanceNoticeFromPolicy = false, deductsPay = false, adminOnly = false, countsCalendarDays = false, sensitive = false,
    };

    private static async Task<JsonElement> GetAllAsync(HttpClient admin) =>
        JsonDocument.Parse(await admin.GetStringAsync("/api/leave-types/all")).RootElement;

    // ── who can read and change ──────────────────────────────────────────────────

    [Fact]
    public async Task EveryoneSignedInReadsTheCatalogue_OnlyPolicyManageSeesUsageAndChanges()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.9.1");
        var employee = await LoginAsync(await HireEmployeeAsync(admin), "Test@1234", "10.0.9.2");

        var read = await employee.GetAsync("/api/leave-types");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var keys = Rows(JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement).Select(r => r.GetProperty("key").GetString()).ToList();
        Assert.Contains("maternity", keys);
        Assert.Contains("annual", keys);

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/leave-types/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync("/api/leave-types", new { version = 0, types = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/leave-types")).StatusCode);
    }

    // ── saving ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavingAddsATypeAndBumpsTheVersion_AStaleSaveIsRefused_InvalidDataIsRejected()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.9.3");
        var before = await GetAllAsync(admin);
        var version = before.GetProperty("version").GetInt32();

        var saved = await admin.PutAsJsonAsync("/api/leave-types", new { version, types = Body(Rows(before), Study()) });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var newVersion = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement.GetProperty("version").GetInt32();
        Assert.Equal(version + 1, newVersion);

        Assert.Contains(Rows(await GetAllAsync(admin)), r => r.GetProperty("key").GetString() == "study");
        // A second administrator still holds the old version.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/leave-types", new { version, types = Body(Rows(before)) })).StatusCode);
        // A repeated key is a 400, and so is a missing system type.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/leave-types", new { version = newVersion, types = Body(Rows(before), Study(), Study()) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/leave-types", new { version = newVersion, types = Body([.. Rows(before).Where(r => r.GetProperty("key").GetString() != "sick")]) })).StatusCode);

        // Put the shared database back: the same types without the extra one.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/leave-types", new { version = newVersion, types = Body(Rows(before)) })).StatusCode);
    }

    // ── the catalogue drives what an employee can do ─────────────────────────────

    [Fact]
    public async Task AnEmployeeCannotApplyForAnAdminOnlyType_ButCanTakeA98DayMaternityStoredAs784Hours()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.9.4");
        var employee = await LoginAsync(await HireEmployeeAsync(admin), "Test@1234", "10.0.9.5");

        // Start on a Monday a few weeks ahead so the 98 days span weekends.
        var start = DateTime.UtcNow.Date.AddDays(28);
        start = start.AddDays(((int)DayOfWeek.Monday - (int)start.DayOfWeek + 7) % 7);
        var end = start.AddDays(97);

        var noShow = await employee.PostAsJsonAsync("/api/leave", new { leaveType = "no-show", startDate = start, endDate = start });
        Assert.Equal(HttpStatusCode.Forbidden, noShow.StatusCode);

        var tooLong = await employee.PostAsJsonAsync("/api/leave", new { leaveType = "maternity", startDate = start, endDate = end.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var ok = await employee.PostAsJsonAsync("/api/leave", new { leaveType = "maternity", startDate = start, endDate = end });
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());

        var mine = JsonDocument.Parse(await employee.GetStringAsync("/api/leave/mine")).RootElement;
        var leave = mine.EnumerateArray().Single();
        Assert.Equal("maternity", leave.GetProperty("leaveType").GetString());
        Assert.Equal("pending", leave.GetProperty("status").GetString());   // a manager decides a long absence
        Assert.Equal(784m, leave.GetProperty("hours").GetDecimal());
    }

    // ── two administrators saving at once ────────────────────────────────────────

    private sealed class SavingAudit(IAppDbContext db) : IAuditService
    {
        public async Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default)
        {
            db.AuditLogs.Add(E.AuditLog.Create(et, ei, a, pid, pn, c));
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed class User : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => "race";
    }

    private static LeaveTypeSpec Spec(E.LeaveTypeDefinition t) => new(t.Key, t.Label, t.Colour, t.SortOrder, t.Active, t.Pool,
        t.AllowanceDays, t.MaxDaysPerRequest, t.AutoApprove, t.AdvanceNoticeDays, t.AdvanceNoticeFromPolicy,
        t.DeductsPay, t.AdminOnly, t.CountsCalendarDays, t.Sensitive);

    [Fact]
    public async Task TwoSavesFromTheSameVersion_ExactlyOneWins_TheOtherIsToldItIsStale()
    {
        int current;
        List<LeaveTypeSpec> baseline;
        await using (var read = fx.NewDb())
        {
            var catalog = await new HRNexus.Application.Leave.GetLeaveTypesQueryHandler(read)
                .Handle(new HRNexus.Application.Leave.GetLeaveTypesQuery(IncludeInactive: true), CancellationToken.None);
            current = catalog.Version;
            baseline = [.. LeaveTypeDefaults.All().Select(Spec)];
        }

        static LeaveTypeSpec Extra(string key) => new(key, key, "info", 100, true, "none", null, null, false, null, false, false, false, false, false);

        async Task<(bool ok, Exception? error)> Save(string key)
        {
            await using var db = fx.NewDb();
            try
            {
                await new SaveLeaveTypesCommandHandler(db, new SavingAudit(db), new User())
                    .Handle(new SaveLeaveTypesCommand(current, [.. baseline, Extra(key)]), CancellationToken.None);
                return (true, null);
            }
            catch (Exception ex) { return (false, ex); }
        }

        var results = await Task.WhenAll(Save("racea"), Save("raceb"));

        Assert.Single(results, r => r.ok);
        var loser = Assert.Single(results, r => !r.ok);
        Assert.IsType<InvalidOperationException>(loser.error);

        // Exactly one of the two extra types exists, at version + 1; then put the shared database back.
        await using var after = fx.NewDb();
        var rows = await after.LeaveTypes.AsNoTracking().ToListAsync();
        Assert.Equal(1, rows.Count(r => r.Key is "racea" or "raceb"));
        Assert.All(rows, r => Assert.Equal(current + 1, r.CatalogVersion));
        await new SaveLeaveTypesCommandHandler(after, new SavingAudit(after), new User())
            .Handle(new SaveLeaveTypesCommand(current + 1, baseline), CancellationToken.None);
    }

    // ── the migration ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCatalogueTableRoundTripsEveryFieldThroughPostgres()
    {
        await using var db = fx.NewDbOn($"lt_{Guid.NewGuid():N}");
        await db.Database.MigrateAsync();
        var all = LeaveTypeDefaults.All();
        db.LeaveTypes.AddRange(all);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = (await db.LeaveTypes.AsNoTracking().ToListAsync()).ToDictionary(t => t.Key);

        Assert.Equal(9, loaded.Count);
        Assert.Equal(60m, loaded["hospitalisation"].AllowanceDays);
        Assert.Equal(98m, loaded["maternity"].MaxDaysPerRequest);
        Assert.True(loaded["maternity"].CountsCalendarDays);
        Assert.True(loaded["maternity"].Sensitive);
        Assert.True(loaded["annual"].AdvanceNoticeFromPolicy);
        Assert.True(loaded["sick"].IsSystem);
        Assert.True(loaded["no-show"].AdminOnly);
        await db.Database.EnsureDeletedAsync();
    }
}
