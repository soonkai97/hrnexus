using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Drives the NEW pay-type + payslip HTTP endpoints end-to-end against REAL Postgres — the layer the
/// handler unit tests (EF InMemory) never reach: authenticated request → query-param model binding
/// (incl. <c>DateOnly</c>) → Npgsql → JSON/CSV response. This is the coverage gap that let earlier
/// controller-wiring bugs (clock-in GPS drop; the UTC-500) slip past InMemory unit tests (issue #3).
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

    /// <summary>Seeds a staff member with one HOURLY shift (8h × 20 = 160) and one FIXED overnight
    /// package shift (12h, flat 500). Returns the human StaffId used by the payslip endpoint.</summary>
    private async Task<string> SeedStaffWithShiftsAsync()
    {
        await using var db = fx.NewDb();
        var bartender = await db.Roles.FirstOrDefaultAsync(r => r.Name == "BARTENDER");
        if (bartender is null) { bartender = E.Role.Create("bartender", hourlyRate: 20m); db.Roles.Add(bartender); }
        var package = await db.Roles.FirstOrDefaultAsync(r => r.Name == "PACKAGE");
        if (package is null) { package = E.Role.Create("package", hourlyRate: 500m, payType: AppConstants.PayType.Fixed); db.Roles.Add(package); }
        await db.SaveChangesAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var staffId = $"PS{suffix}"[..10];
        var staff = E.Staff.Create(staffId, "Pay", "Slip", $"{suffix}@t.com", "hash",
            new List<Guid> { bartender.Id, package.Id });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        var hourly = E.WorkLog.StartLive(staff.Id, "BARTENDER", "loc1", "Main",
            new DateOnly(2026, 4, 5), "09:00", 20m);
        hourly.End("17:00");                         // 8h
        var fixedPkg = E.WorkLog.StartLive(staff.Id, "PACKAGE", "loc1", "Main",
            new DateOnly(2026, 4, 6), "20:00", 500m, payType: AppConstants.PayType.Fixed);
        fixedPkg.End("08:00");                        // 12h overnight, flat 500
        db.WorkLogs.Add(hourly);
        db.WorkLogs.Add(fixedPkg);
        await db.SaveChangesAsync();
        // Return the STORED StaffId — Staff.Create upper-cases it, and the payslip lookup is
        // case-sensitive (the real frontend always passes this canonical value from the summary).
        return staff.StaffId;
    }

    [Fact]
    public async Task Payslip_Returns200_WithPerShiftRows_FixedPricedFlat_AndTotalsReconcile()
    {
        var staffId = await SeedStaffWithShiftsAsync();
        var client  = await AdminClientAsync("10.0.9.1");

        // DateOnly query params must bind from the query string for this to even reach the handler.
        var resp = await client.GetAsync($"/api/admin/payroll/payslip?staffId={staffId}&from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(staffId, root.GetProperty("staffId").GetString());

        var shifts = root.GetProperty("shifts");
        Assert.Equal(2, shifts.GetArrayLength());

        // Day-ordered: hourly 04-05 first, then the fixed package 04-06.
        var s0 = shifts[0];
        Assert.Equal("2026-04-05", s0.GetProperty("workDate").GetString());
        Assert.Equal("hourly", s0.GetProperty("payType").GetString());
        Assert.Equal(8m,   s0.GetProperty("hours").GetDecimal());
        Assert.Equal(160m, s0.GetProperty("pay").GetDecimal());   // 8h × 20

        var s1 = shifts[1];
        Assert.Equal("2026-04-06", s1.GetProperty("workDate").GetString());
        Assert.Equal("fixed", s1.GetProperty("payType").GetString());
        Assert.Equal(12m,  s1.GetProperty("hours").GetDecimal());   // hours still recorded
        Assert.Equal(500m, s1.GetProperty("pay").GetDecimal());     // but pay is flat, NOT 12 × 500

        // Totals reconcile with the listed rows (and with the summary's Gross Pay).
        Assert.Equal(20m,  root.GetProperty("totalHours").GetDecimal());
        Assert.Equal(660m, root.GetProperty("totalPay").GetDecimal());
    }

    [Fact]
    public async Task Payslip_DeductsOutstandingAdvance_ForNetPayable_AndGroupsJobs()
    {
        var staffId = await SeedStaffWithShiftsAsync();   // gross 660 (160 hourly + 500 fixed)

        // Record an outstanding advance of 200 against the same staff member.
        await using (var seed = fx.NewDb())
        {
            var emp = await seed.Staff.FirstAsync(s => s.StaffId == staffId);
            seed.CashAdvances.Add(E.CashAdvance.Create(emp.Id, 200m, new DateOnly(2026, 4, 2), "advance", "admin"));
            await seed.SaveChangesAsync();
        }

        var client = await AdminClientAsync("10.0.9.6");
        var resp = await client.GetAsync($"/api/admin/payroll/payslip?staffId={staffId}&from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(660m, root.GetProperty("totalPay").GetDecimal());
        Assert.Equal(200m, root.GetProperty("outstandingAdvance").GetDecimal());
        Assert.Equal(460m, root.GetProperty("netPayable").GetDecimal());        // 660 − 200
        Assert.Equal(2,    root.GetProperty("jobBreakdown").GetArrayLength());    // BARTENDER + PACKAGE
    }

    [Fact]
    public async Task PayslipExport_ReturnsCsv_WithTotalRow()
    {
        var staffId = await SeedStaffWithShiftsAsync();
        var client  = await AdminClientAsync("10.0.9.2");

        var resp = await client.GetAsync($"/api/admin/payroll/payslip/export?staffId={staffId}&from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/csv", resp.Content.Headers.ContentType?.MediaType);

        var csv = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Work Date,Start,End Date", csv);   // header
        Assert.Contains("2026-04-05", csv);                  // the hourly shift
        Assert.Contains("GROSS TOTAL", csv);                 // trailing summary rows
        Assert.Contains("NET PAYABLE", csv);
    }

    [Fact]
    public async Task Payslip_Returns400_WhenRangeInverted()
    {
        var staffId = await SeedStaffWithShiftsAsync();
        var client  = await AdminClientAsync("10.0.9.3");

        var resp = await client.GetAsync($"/api/admin/payroll/payslip?staffId={staffId}&from=2026-04-30&to=2026-04-01");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Payslip_Returns404_WhenStaffMissing()
    {
        var client = await AdminClientAsync("10.0.9.4");
        var resp = await client.GetAsync("/api/admin/payroll/payslip?staffId=NOBODY&from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
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
