using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Application.WorkLog.Queries;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Tests for three handlers that were not covered by previous test files:
///   - GetPaySummaryQueryHandler  : staff-facing pay breakdown with OT calculation and tips
///   - GetPayrollExportQueryHandler : admin CSV-ready payroll per employee
///   - GetLiveStatsQueryHandler   : real-time dashboard counters (on-shift, total staff, hours, cost)
/// WHY: incorrect pay figures directly mislead staff and managers; live-stats errors show
/// wrong occupancy on the dashboard, hiding potential compliance issues.
/// </summary>
public class RemainingQueryTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    /// <summary>Fixed-time provider so tests do not depend on wall-clock time.</summary>
    private sealed class FixedDateTimeProvider(DateTime fixedLocal) : IDateTimeProvider
    {
        public DateTime     UtcNow        => fixedLocal.ToUniversalTime();
        public DateOnly     TodayUtc      => DateOnly.FromDateTime(UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime     LocalNow      => fixedLocal;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static E.Staff MakeStaff(string staffId = "S001", List<string>? roles = null) =>
        E.Staff.Create(staffId, "Alice", "Wong", $"{staffId}@test.com", "hash",
            roles ?? ["BARTENDER"]);

    private static E.WorkLog MakeCompletedLog(
        Guid empId, DateOnly date, string start, string end, decimal rate = 20m) =>
        E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", date, start, end, rate);

    // ══════════════════════════════════════════════════════════════════════════
    // GetPaySummaryQuery
    // ══════════════════════════════════════════════════════════════════════════

    // 1. No logs → all zeros
    [Fact]
    public async Task GetPaySummary_ReturnsZeros_WhenNoLogsInPeriod()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var result = await new GetPaySummaryQueryHandler(db).Handle(
            new GetPaySummaryQuery(empId,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31)),
            CancellationToken.None);

        Assert.Equal(0m, result.TotalHours);
        Assert.Equal(0m, result.GrossPay);
        Assert.Empty(result.Shifts);
    }

    // 2. Single shift — hours and pay calculated correctly
    [Fact]
    public async Task GetPaySummary_CalculatesHoursAndPay_ForCompletedShift()
    {
        await using var db = CreateDb();
        var emp = MakeStaff();
        db.Staff.Add(emp);
        // 18:00 → 23:00 = 5 hours @ 20/h = 100
        var log = MakeCompletedLog(emp.Id, new DateOnly(2026, 5, 10), "18:00", "23:00", 20m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        var result = await new GetPaySummaryQueryHandler(db).Handle(
            new GetPaySummaryQuery(emp.Id,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31)),
            CancellationToken.None);

        Assert.Equal(5m,   result.TotalHours);
        Assert.Equal(100m, result.GrossPay);
        Assert.Single(result.Shifts);
    }

    // 3. Open shift (no EndTime) is excluded from pay summary
    [Fact]
    public async Task GetPaySummary_ExcludesOpenShifts()
    {
        await using var db = CreateDb();
        var emp = MakeStaff();
        db.Staff.Add(emp);
        // Open shift — StartLive sets EndTime = null
        db.WorkLogs.Add(E.WorkLog.StartLive(emp.Id, "BARTENDER", "loc-1", "Main Bar",
            new DateOnly(2026, 5, 10), "18:00", 20m));
        await db.SaveChangesAsync();

        var result = await new GetPaySummaryQueryHandler(db).Handle(
            new GetPaySummaryQuery(emp.Id,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31)),
            CancellationToken.None);

        Assert.Equal(0m, result.TotalHours);
        Assert.Empty(result.Shifts);
    }

    // 4. Pay = total shift hours × hourly rate (straight time, no overtime tiers)
    [Fact]
    public async Task GetPaySummary_PaysAllHoursAtHourlyRate()
    {
        await using var db = CreateDb();
        var emp = MakeStaff();
        db.Staff.Add(emp);
        // 10 hours @ 20/h = 200 (no overtime)
        var log = MakeCompletedLog(emp.Id, new DateOnly(2026, 5, 10), "08:00", "18:00", 20m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        var result = await new GetPaySummaryQueryHandler(db).Handle(
            new GetPaySummaryQuery(emp.Id,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31)),
            CancellationToken.None);

        Assert.Equal(10m,  result.TotalHours);
        Assert.Equal(200m, result.GrossPay);
    }

    // 6. Logs outside the requested period are excluded
    [Fact]
    public async Task GetPaySummary_ExcludesLogsOutsidePeriod()
    {
        await using var db = CreateDb();
        var emp = MakeStaff();
        db.Staff.Add(emp);

        // Log in April — outside the May query
        db.WorkLogs.Add(MakeCompletedLog(emp.Id, new DateOnly(2026, 4, 30), "18:00", "23:00", 20m));
        await db.SaveChangesAsync();

        var result = await new GetPaySummaryQueryHandler(db).Handle(
            new GetPaySummaryQuery(emp.Id,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31)),
            CancellationToken.None);

        Assert.Equal(0m, result.TotalHours);
        Assert.Empty(result.Shifts);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GetPayrollExportQuery
    // ══════════════════════════════════════════════════════════════════════════

    // 1. No active employees → empty rows
    [Fact]
    public async Task GetPayrollExport_ReturnsEmptyRows_WhenNoActiveEmployees()
    {
        await using var db = CreateDb();

        var result = await new GetPayrollExportQueryHandler(db).Handle(
            new GetPayrollExportQuery(2026, 5), CancellationToken.None);

        Assert.Equal("2026-05", result.Month);
        Assert.Empty(result.Rows);
    }

    // 2. Inactive employees are excluded
    [Fact]
    public async Task GetPayrollExport_ExcludesInactiveEmployees()
    {
        await using var db = CreateDb();
        var emp = MakeStaff();
        emp.Deactivate();
        db.Staff.Add(emp);
        await db.SaveChangesAsync();

        var result = await new GetPayrollExportQueryHandler(db).Handle(
            new GetPayrollExportQuery(2026, 5), CancellationToken.None);

        Assert.Empty(result.Rows);
    }

    // 3. Active employee with completed shifts → row with correct hours and pay
    [Fact]
    public async Task GetPayrollExport_CalculatesRegularPayRow_ForActiveEmployee()
    {
        await using var db = CreateDb();
        var emp = MakeStaff("E001");
        db.Staff.Add(emp);
        // 5h shift @ 20/h = 100 regular pay, no OT
        db.WorkLogs.Add(MakeCompletedLog(emp.Id, new DateOnly(2026, 5, 10), "18:00", "23:00", 20m));
        await db.SaveChangesAsync();

        var result = await new GetPayrollExportQueryHandler(db).Handle(
            new GetPayrollExportQuery(2026, 5), CancellationToken.None);

        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal("E001",  row.StaffId);
        Assert.Equal(5m,      row.TotalHours);
        Assert.Equal(100m,    row.GrossPay);   // 5h × RM20 = RM100
    }

    // 4. Open shifts (no EndTime) are excluded from payroll
    [Fact]
    public async Task GetPayrollExport_ExcludesOpenShifts_FromPay()
    {
        await using var db = CreateDb();
        var emp = MakeStaff();
        db.Staff.Add(emp);
        db.WorkLogs.Add(E.WorkLog.StartLive(emp.Id, "BARTENDER", "loc-1", "Main Bar",
            new DateOnly(2026, 5, 10), "18:00", 20m));
        await db.SaveChangesAsync();

        var result = await new GetPayrollExportQueryHandler(db).Handle(
            new GetPayrollExportQuery(2026, 5), CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Equal(0m, result.Rows[0].TotalHours);
        Assert.Equal(0m, result.Rows[0].GrossPay);
    }

    // 5. ALL leave is unpaid — approved leave (any type) adds NO pay; it only shows as days taken.
    [Fact]
    public async Task GetPayrollExport_LeaveIsUnpaid_AddsNoPay_OnlyCountsDays()
    {
        await using var db = CreateDb();
        var emp = MakeStaff();
        db.Staff.Add(emp);
        // 5h shift @ 20/h = 100 work pay — the only pay this employee should receive.
        db.WorkLogs.Add(MakeCompletedLog(emp.Id, new DateOnly(2026, 5, 5), "18:00", "23:00", 20m));
        var annualLeave = E.LeaveRequest.Create(emp.Id, "annual",
            new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 20, 23, 59, 59, DateTimeKind.Utc), null, null, null, null);
        annualLeave.Update("annual",
            new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 20, 23, 59, 59, DateTimeKind.Utc), null, null, null, null, "approved");
        var unpaidLeave = E.LeaveRequest.Create(emp.Id, "unpaid",
            new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 21, 23, 59, 59, DateTimeKind.Utc), null, null, null, null);
        unpaidLeave.Update("unpaid",
            new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 21, 23, 59, 59, DateTimeKind.Utc), null, null, null, null, "approved");
        db.LeaveRequests.AddRange(annualLeave, unpaidLeave);
        await db.SaveChangesAsync();

        var result = await new GetPayrollExportQueryHandler(db).Handle(
            new GetPayrollExportQuery(2026, 5), CancellationToken.None);

        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(100m, row.GrossPay);          // work pay only — annual leave adds nothing
        Assert.Equal(2, row.ApprovedLeaveDays);    // annual + unpaid both counted as days taken (unpaid)
    }

    // 6. Month label is formatted correctly
    [Fact]
    public async Task GetPayrollExport_ReturnsCorrectMonthLabel()
    {
        await using var db = CreateDb();

        var result = await new GetPayrollExportQueryHandler(db).Handle(
            new GetPayrollExportQuery(2026, 12), CancellationToken.None);

        Assert.Equal("2026-12", result.Month);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GetLiveStatsQuery
    // ══════════════════════════════════════════════════════════════════════════

    // 1. Empty DB → all zeros
    [Fact]
    public async Task GetLiveStats_ReturnsZeros_WhenDbIsEmpty()
    {
        await using var db = CreateDb();
        var fixedNow = new DateTime(2026, 5, 10, 21, 0, 0, DateTimeKind.Local);

        var result = await new GetLiveStatsQueryHandler(db, new FixedDateTimeProvider(fixedNow))
            .Handle(new GetLiveStatsQuery(), CancellationToken.None);

        Assert.Equal(0, result.OnShiftCount);
        Assert.Equal(0, result.TotalStaff);
        Assert.Equal(0m, result.TonightHours);
        Assert.Equal(0m, result.EstimatedLabourCost);
    }

    // 2. Active staff count is correct; inactive staff not counted
    [Fact]
    public async Task GetLiveStats_CountsOnlyActiveStaff()
    {
        await using var db = CreateDb();
        var fixedNow = new DateTime(2026, 5, 10, 21, 0, 0, DateTimeKind.Local);
        var today    = DateOnly.FromDateTime(fixedNow);

        var active   = MakeStaff("S001");
        var inactive = MakeStaff("S002");
        inactive.Deactivate();
        db.Staff.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var result = await new GetLiveStatsQueryHandler(db, new FixedDateTimeProvider(fixedNow))
            .Handle(new GetLiveStatsQuery(), CancellationToken.None);

        Assert.Equal(1, result.TotalStaff);
    }

    // 3. Open shift today → counted in OnShiftCount, hours calculated using current time
    [Fact]
    public async Task GetLiveStats_CountsOpenShifts_AndCalculatesRunningHours()
    {
        await using var db = CreateDb();
        // Current local time: 22:00. Open shift started 20:00 → 2 hours running @ 20/h = 40
        var fixedNow = new DateTime(2026, 5, 10, 22, 0, 0, DateTimeKind.Local);
        var today    = DateOnly.FromDateTime(fixedNow);

        var emp = MakeStaff();
        db.Staff.Add(emp);
        db.WorkLogs.Add(E.WorkLog.StartLive(emp.Id, "BARTENDER", "loc-1", "Main Bar",
            today, "20:00", 20m));
        await db.SaveChangesAsync();

        var result = await new GetLiveStatsQueryHandler(db, new FixedDateTimeProvider(fixedNow))
            .Handle(new GetLiveStatsQuery(), CancellationToken.None);

        Assert.Equal(1, result.OnShiftCount);
        Assert.Equal(2m, result.TonightHours);
        Assert.Equal(40m, result.EstimatedLabourCost);
    }

    // 4. Completed shift today → excluded from OnShiftCount but included in hours/cost
    [Fact]
    public async Task GetLiveStats_ExcludesCompletedShifts_FromOnShiftCount_ButIncludesInHours()
    {
        await using var db = CreateDb();
        var fixedNow = new DateTime(2026, 5, 10, 23, 0, 0, DateTimeKind.Local);
        var today    = DateOnly.FromDateTime(fixedNow);

        var emp = MakeStaff();
        db.Staff.Add(emp);
        // Completed 5h shift 18:00–23:00 @ 20/h = 100
        db.WorkLogs.Add(MakeCompletedLog(emp.Id, today, "18:00", "23:00", 20m));
        await db.SaveChangesAsync();

        var result = await new GetLiveStatsQueryHandler(db, new FixedDateTimeProvider(fixedNow))
            .Handle(new GetLiveStatsQuery(), CancellationToken.None);

        Assert.Equal(0,    result.OnShiftCount);   // shift is done
        Assert.Equal(5m,   result.TonightHours);
        Assert.Equal(100m, result.EstimatedLabourCost);
    }

    // 5. Yesterday's logs are not counted in tonight's stats
    [Fact]
    public async Task GetLiveStats_ExcludesYesterdaysLogs()
    {
        await using var db = CreateDb();
        var fixedNow = new DateTime(2026, 5, 10, 22, 0, 0, DateTimeKind.Local);
        var yesterday = DateOnly.FromDateTime(fixedNow).AddDays(-1);

        var emp = MakeStaff();
        db.Staff.Add(emp);
        db.WorkLogs.Add(MakeCompletedLog(emp.Id, yesterday, "18:00", "23:00", 20m));
        await db.SaveChangesAsync();

        var result = await new GetLiveStatsQueryHandler(db, new FixedDateTimeProvider(fixedNow))
            .Handle(new GetLiveStatsQuery(), CancellationToken.None);

        Assert.Equal(0m, result.TonightHours);
        Assert.Equal(0m, result.EstimatedLabourCost);
    }
}
