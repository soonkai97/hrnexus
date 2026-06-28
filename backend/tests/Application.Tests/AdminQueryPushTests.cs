using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Push;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for:
/// - GetDashboardQuery
/// - GetReportQuery
/// - GetLeaveQuery
/// - GetLeaveBalanceQuery
/// - GetAllLeaveBalancesQuery
/// - GetOvertimeRulesQuery
/// - SavePushSubscriptionCommand
/// - RemovePushSubscriptionCommand
/// </summary>
public class AdminQueryPushTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class FixedDateTimeProvider(DateTime utcNow, TimeZoneInfo? tz = null) : IDateTimeProvider
    {
        public DateTime     UtcNow       => utcNow;
        public DateOnly     TodayUtc     => DateOnly.FromDateTime(utcNow);
        public TimeZoneInfo VenueTimeZone => tz ?? TimeZoneInfo.Utc;
        public DateTime     LocalNow     => TimeZoneInfo.ConvertTimeFromUtc(utcNow, VenueTimeZone);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static E.Staff MakeStaff(string staffId = "S001", bool active = true)
    {
        var s = E.Staff.Create(staffId, "Alice", "Wong", $"{staffId}@test.com", "hash",
            new List<string> { "BARTENDER" });
        if (!active) s.Deactivate();
        return s;
    }

    // Fixed UTC reference used across dashboard tests.
    private static readonly DateTime UtcRef = new(2025, 6, 15, 14, 0, 0, DateTimeKind.Utc);

    // ═════════════════════════════════════════════════════════════════════════
    // GetDashboardQuery
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDashboard_ReturnsZeroStats_WhenNoData()
    {
        await using var db  = CreateDb();
        var dt              = new FixedDateTimeProvider(UtcRef);
        var handler         = new GetDashboardQueryHandler(db, dt);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.Equal(0, result.Stats.OnShiftCount);
        Assert.Equal(0, result.Stats.TotalStaff);
        Assert.Empty(result.CurrentlyOnShift);
        Assert.Empty(result.RecentActivity);
    }

    [Fact]
    public async Task GetDashboard_CountsActiveStaff_WhenStaffExists()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001"));
        db.Staff.Add(MakeStaff("S002"));
        // Inactive staff should NOT be counted in TotalStaff
        db.Staff.Add(MakeStaff("S003", active: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(UtcRef);
        var handler = new GetDashboardQueryHandler(db, dt);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.Equal(2, result.Stats.TotalStaff);
    }

    [Fact]
    public async Task GetDashboard_ReportsOnShiftStaff_WhenOpenShiftExists()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        // Today in UTC date
        var today = DateOnly.FromDateTime(UtcRef);
        var log = E.WorkLog.StartLive(staff.Id, "BARTENDER", "loc1", "Main Hall",
            today, "12:00", 20m, notes: "covering front desk");
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(UtcRef);
        var handler = new GetDashboardQueryHandler(db, dt);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.Equal(1, result.Stats.OnShiftCount);
        Assert.Single(result.CurrentlyOnShift);
        Assert.Equal("S001", result.CurrentlyOnShift[0].StaffId);
        Assert.Equal("Main Hall", result.CurrentlyOnShift[0].LocationName);
        // The shift note surfaces on the dashboard's on-shift list, not just in Reports.
        Assert.Equal("covering front desk", result.CurrentlyOnShift[0].Notes);
    }

    [Fact]
    public async Task GetDashboard_OpenShiftsCount_OnlyCountsStaleShifts()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var today     = DateOnly.FromDateTime(UtcRef);
        var yesterday = today.AddDays(-1);

        // Today's open shift — NOT stale
        db.WorkLogs.Add(E.WorkLog.StartLive(staff.Id, "BARTENDER", "loc1", "Main",
            today, "22:00", 20m));
        // Yesterday's open shift — stale (forgot to clock out)
        db.WorkLogs.Add(E.WorkLog.StartLive(staff.Id, "BARTENDER", "loc1", "Main",
            yesterday, "22:00", 20m));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(UtcRef);
        var handler = new GetDashboardQueryHandler(db, dt);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        // Total on-shift = 2 (both open), but stale = 1
        Assert.Equal(2, result.Stats.OnShiftCount);
        Assert.Equal(1, result.Stats.OpenShiftsCount);
    }

    [Fact]
    public async Task GetDashboard_RecentActivity_OnlyContainsTodaysLogs()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var today     = DateOnly.FromDateTime(UtcRef);
        var yesterday = today.AddDays(-1);

        // Completed shift today
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc1", "Main",
            today, "09:00", "17:00", 20m));
        // Completed shift yesterday — should NOT appear in RecentActivity
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc1", "Main",
            yesterday, "09:00", "17:00", 20m));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(UtcRef);
        var handler = new GetDashboardQueryHandler(db, dt);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.Single(result.RecentActivity);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetReportQuery
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetReport_ReturnsEmptyResponse_WhenNoLogsExist()
    {
        await using var db  = CreateDb();
        var handler         = new GetReportQueryHandler(db);
        var from            = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to              = new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var result = await handler.Handle(
            new GetReportQuery(from, to, null, null, null),
            CancellationToken.None);

        Assert.Equal(0, result.Summary.TotalShifts);
        Assert.Empty(result.Shifts);
        Assert.Empty(result.ByStaff);
    }

    [Fact]
    public async Task GetReport_ReturnsSummary_WhenCompletedShiftsExist()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var workDate = new DateOnly(2025, 6, 1);
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc1", "Main Hall",
            workDate, "09:00", "17:00", 20m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetReportQueryHandler(db);
        var result  = await handler.Handle(
            new GetReportQuery(
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null, null, null),
            CancellationToken.None);

        Assert.Equal(1, result.Summary.TotalShifts);
        Assert.Equal(8m, result.Summary.TotalHours);
        Assert.Single(result.Shifts);
        Assert.Equal("Main Hall", result.Shifts[0].LocationName);
    }

    [Fact]
    public async Task GetReport_ExcludesOpenShifts()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var workDate = new DateOnly(2025, 6, 1);
        // Active (open) shift — EndTime is null → must be excluded from report
        db.WorkLogs.Add(E.WorkLog.StartLive(staff.Id, "BARTENDER", "loc1", "Main",
            workDate, "09:00", 20m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetReportQueryHandler(db);
        var result  = await handler.Handle(
            new GetReportQuery(
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null, null, null),
            CancellationToken.None);

        Assert.Equal(0, result.Summary.TotalShifts);
        Assert.Empty(result.Shifts);
    }

    [Fact]
    public async Task GetReport_FiltersBy_StaffId()
    {
        await using var db = CreateDb();
        var staff1 = MakeStaff("S001");
        var staff2 = MakeStaff("S002");
        db.Staff.AddRange(staff1, staff2);

        var workDate = new DateOnly(2025, 6, 1);
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff1.Id, "BARTENDER", "loc1", "Main",
            workDate, "09:00", "17:00", 20m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff2.Id, "BARTENDER", "loc1", "Main",
            workDate, "09:00", "17:00", 20m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetReportQueryHandler(db);
        var result  = await handler.Handle(
            new GetReportQuery(
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new List<Guid> { staff1.Id }, null, null),
            CancellationToken.None);

        Assert.Equal(1, result.Summary.TotalShifts);
        Assert.All(result.Shifts, s => Assert.Equal(staff1.StaffId, s.StaffId));
    }

    [Fact]
    public async Task GetReport_FiltersBy_LocationName()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var workDate = new DateOnly(2025, 6, 1);
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc1", "VIP Room",
            workDate, "09:00", "17:00", 20m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc2", "Main Hall",
            workDate, "09:00", "17:00", 20m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetReportQueryHandler(db);
        var result  = await handler.Handle(
            new GetReportQuery(
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null, new List<string> { "VIP Room" }, null),
            CancellationToken.None);

        Assert.Equal(1, result.Summary.TotalShifts);
        Assert.Equal("VIP Room", result.Shifts[0].LocationName);
    }

    [Fact]
    public async Task GetReport_FiltersBy_DateRange()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc1", "Main",
            new DateOnly(2025, 5, 31), "09:00", "17:00", 20m)); // before range
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc1", "Main",
            new DateOnly(2025, 6, 1), "09:00", "17:00", 20m)); // in range
        db.WorkLogs.Add(E.WorkLog.CreateManual(staff.Id, "BARTENDER", "loc1", "Main",
            new DateOnly(2025, 6, 2), "09:00", "17:00", 20m)); // after range
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetReportQueryHandler(db);
        var result  = await handler.Handle(
            new GetReportQuery(
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null, null, null),
            CancellationToken.None);

        Assert.Equal(1, result.Summary.TotalShifts);
        Assert.Equal("2025-06-01", result.Shifts[0].WorkDate);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetLeaveQuery
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLeave_ReturnsEmpty_WhenNoLeaveRequests()
    {
        await using var db  = CreateDb();
        var handler         = new GetLeaveQueryHandler(db);

        var result = await handler.Handle(
            new GetLeaveQuery(null, null, null),
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task GetLeave_ReturnsAllRequests_WhenNoFilters()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var start = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, "annual", start, start, null, null, null, null));
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, "sick",   start, start, null, null, null, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetLeaveQueryHandler(db);
        var result  = await handler.Handle(
            new GetLeaveQuery(null, null, null, null, null, 1, 50),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetLeave_FiltersBy_EmployeeId()
    {
        await using var db = CreateDb();
        var staff1 = MakeStaff("S001");
        var staff2 = MakeStaff("S002");
        db.Staff.AddRange(staff1, staff2);

        var start = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff1.Id, "annual", start, start, null, null, null, null));
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff2.Id, "annual", start, start, null, null, null, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetLeaveQueryHandler(db);
        var result  = await handler.Handle(
            new GetLeaveQuery(staff1.Id, null, null),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.All(result.Items, item => Assert.Equal(staff1.Id, item.EmployeeId));
    }

    [Fact]
    public async Task GetLeave_FiltersBy_Status()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var start   = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var pending  = E.LeaveRequest.Create(staff.Id, "annual", start, start, null, null, null, null);
        var approved = E.LeaveRequest.Create(staff.Id, "annual", start, start, null, null, null, null);
        approved.Update("annual", start, start, null, null, null, null, "approved");

        db.LeaveRequests.AddRange(pending, approved);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetLeaveQueryHandler(db);
        var result  = await handler.Handle(
            new GetLeaveQuery(null, null, null, "pending"),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("pending", result.Items[0].Status);
    }

    [Fact]
    public async Task GetLeave_PendingAppearFirst_InResults()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var start    = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var approved = E.LeaveRequest.Create(staff.Id, "annual", start, start, null, null, null, null);
        approved.Update("annual", start, start, null, null, null, null, "approved");
        var pending  = E.LeaveRequest.Create(staff.Id, "sick", start, start, null, null, null, null);

        db.LeaveRequests.AddRange(approved, pending);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetLeaveQueryHandler(db);
        var result  = await handler.Handle(
            new GetLeaveQuery(null, null, null, null, null, 1, 50),
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        // Pending must be first
        Assert.Equal("pending", result.Items[0].Status);
    }

    [Fact]
    public async Task GetLeave_ReturnsCorrectDto_Fields()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var start = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc);
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, "annual", start, end, "09:00", "17:00", 24m, "Holiday"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetLeaveQueryHandler(db);
        var result  = await handler.Handle(
            new GetLeaveQuery(null, null, null, null, null, 1, 50),
            CancellationToken.None);

        var dto = result.Items[0];
        Assert.Equal("S001", dto.StaffId);
        Assert.Equal("annual", dto.LeaveType);
        Assert.Equal("2025-06-01", dto.StartDate);
        Assert.Equal("2025-06-03", dto.EndDate);
        Assert.Equal(24m, dto.Hours); // explicit hours stored
        Assert.Equal("09:00", dto.StartTime);
        Assert.Equal("17:00", dto.EndTime);
        Assert.Equal("Holiday", dto.Notes);
    }

    [Fact]
    public async Task GetLeave_FiltersBy_LeaveType_ReturnsOnlySick()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var start = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, "sick",   start, start, null, null, null, null));
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, "annual", start, start, null, null, null, null));
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, "unpaid", start, start, null, null, null, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetLeaveQueryHandler(db);
        var result  = await handler.Handle(
            new GetLeaveQuery(null, null, null, null, "sick", 1, 50),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.All(result.Items, item => Assert.Equal("sick", item.LeaveType));
    }

    [Fact]
    public async Task GetLeave_PendingItems_OrderedBy_CreatedAt_Ascending()
    {
        // Oldest application (earliest CreatedAt) must appear first so managers
        // review leave in the order it was submitted — fairness requirement.
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        var start = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Insert newer pending first to verify the sort is not insertion order
        var newer = E.LeaveRequest.Create(staff.Id, "annual", start, start, null, null, null, null);
        await db.SaveChangesAsync(); // flush so CreatedAt timestamps differ

        var older = E.LeaveRequest.Create(staff.Id, "sick", start, start, null, null, null, null);
        db.LeaveRequests.AddRange(newer, older);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetLeaveQueryHandler(db);
        var result  = await handler.Handle(
            new GetLeaveQuery(null, null, null, "pending", null, 1, 50),
            CancellationToken.None);

        // Both are pending; the one created first (older.CreatedAt < newer.CreatedAt) must be first
        Assert.Equal(2, result.Items.Count);
        var first  = result.Items[0];
        var second = result.Items[1];
        Assert.True(
            DateTime.Parse(first.CreatedAt) <= DateTime.Parse(second.CreatedAt),
            "Pending leave records must be ordered oldest-created-first");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetLeaveBalanceQuery
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLeaveBalance_ReturnsDefaults_WhenNoEntitlementOrLeaveExists()
    {
        await using var db  = CreateDb();
        var empId           = Guid.NewGuid();
        var dt              = new FixedDateTimeProvider(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler         = new GetLeaveBalanceQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveBalanceQuery(empId, 2025), CancellationToken.None);

        Assert.Equal(2025,  result.Year);
        Assert.Equal(0m,    result.AnnualEntitlement);   // 0 = not yet set
        Assert.Equal(0m,    result.AnnualUsed);
        Assert.Equal(0m,    result.AnnualRemaining);
        Assert.Equal(0m,    result.SickEntitlement);     // 0 = not yet set
        Assert.Equal(0m,    result.SickUsed);
        Assert.Equal(0m,    result.SickRemaining);
        Assert.Equal(0m,    result.UnpaidUsed);
    }

    [Fact]
    public async Task GetLeaveBalance_ReturnsCustomEntitlement_WhenEntitlementExists()
    {
        await using var db = CreateDb();
        var empId          = Guid.NewGuid();
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(empId, 2025, annualHours: 20m, sickHours: 10m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveBalanceQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveBalanceQuery(empId, 2025), CancellationToken.None);

        Assert.Equal(20m, result.AnnualEntitlement);
        Assert.Equal(10m, result.SickEntitlement);
    }

    [Fact]
    public async Task GetLeaveBalance_DeductsUsedLeave_FromBalance()
    {
        await using var db = CreateDb();
        var empId          = Guid.NewGuid();

        // 16 hours annual leave (pending, counts towards balance)
        var start = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2025, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(empId, 2025, annualHours: 80m, sickHours: 40m));
        db.LeaveRequests.Add(E.LeaveRequest.Create(empId, "annual", start, end, null, null, 16m, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveBalanceQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveBalanceQuery(empId, 2025), CancellationToken.None);

        Assert.Equal(16m, result.AnnualUsed);
        Assert.Equal(64m, result.AnnualRemaining);
    }

    [Fact]
    public async Task GetLeaveBalance_DoesNotCountRejectedLeave()
    {
        await using var db = CreateDb();
        var empId          = Guid.NewGuid();

        var start    = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(empId, 2025, annualHours: 40m, sickHours: 20m));
        var rejected = E.LeaveRequest.Create(empId, "annual", start, start, null, null, 8m, null);
        rejected.Update("annual", start, start, null, null, 8m, null, "rejected");
        db.LeaveRequests.Add(rejected);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveBalanceQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveBalanceQuery(empId, 2025), CancellationToken.None);

        Assert.Equal(0m,  result.AnnualUsed);
        Assert.Equal(40m, result.AnnualRemaining);
    }

    [Fact]
    public async Task GetLeaveBalance_UsesCurrentYear_WhenYearParamIsNull()
    {
        await using var db = CreateDb();
        var empId          = Guid.NewGuid();

        var dt      = new FixedDateTimeProvider(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveBalanceQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveBalanceQuery(empId, null), CancellationToken.None);

        Assert.Equal(2026, result.Year);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetAllLeaveBalancesQuery
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAllLeaveBalances_ReturnsEmpty_WhenNoActiveStaff()
    {
        await using var db  = CreateDb();
        var dt              = new FixedDateTimeProvider(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler         = new GetAllLeaveBalancesQueryHandler(db, dt);

        var result = await handler.Handle(new GetAllLeaveBalancesQuery(2025), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllLeaveBalances_ReturnsOneRowPerActiveStaff()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001"));
        db.Staff.Add(MakeStaff("S002"));
        db.Staff.Add(MakeStaff("S003", active: false)); // inactive — should not appear
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetAllLeaveBalancesQueryHandler(db, dt);

        var result = await handler.Handle(new GetAllLeaveBalancesQuery(2025), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.StaffId == "S003");
    }

    [Fact]
    public async Task GetAllLeaveBalances_IncludesAllActiveStaff_RegardlessOfRole()
    {
        // Superadmins are treated as normal staff — they appear in the leave overview like anyone else.
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001"));   // BARTENDER
        db.Staff.Add(E.Staff.Create("ADMIN1", "Super", "Admin", "admin1@test.com", "hash",
            new List<string> { "SUPERADMIN" }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetAllLeaveBalancesQueryHandler(db, dt);

        var result = await handler.Handle(new GetAllLeaveBalancesQuery(2025), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.StaffId == "ADMIN1");
    }

    [Fact]
    public async Task GetAllLeaveBalances_ShowsUsedLeave_ForEachEmployee()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("S001");
        db.Staff.Add(staff);

        // 24 hours annual leave approved
        var start    = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var end      = new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 80m, sickHours: 40m));
        var approved = E.LeaveRequest.Create(staff.Id, "annual", start, end, null, null, 24m, null);
        approved.Update("annual", start, end, null, null, 24m, null, "approved");
        db.LeaveRequests.Add(approved);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt      = new FixedDateTimeProvider(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetAllLeaveBalancesQueryHandler(db, dt);

        var result = await handler.Handle(new GetAllLeaveBalancesQuery(2025), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(24m, result[0].AnnualUsed);
        Assert.Equal(56m, result[0].AnnualRemaining);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SavePushSubscriptionCommand
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SavePushSubscription_Adds_WhenNotExists()
    {
        await using var db  = CreateDb();
        var empId           = Guid.NewGuid();
        var handler         = new SavePushSubscriptionCommandHandler(db);

        await handler.Handle(
            new SavePushSubscriptionCommand(empId, "https://push.example.com/abc", "key123", "auth123"),
            CancellationToken.None);

        Assert.Equal(1, await db.PushSubscriptions.CountAsync());
        var sub = await db.PushSubscriptions.FirstAsync();
        Assert.Equal(empId,    sub.EmployeeId);
        Assert.Equal("https://push.example.com/abc", sub.Endpoint);
    }

    [Fact]
    public async Task SavePushSubscription_IsIdempotent_WhenEndpointAlreadyExists()
    {
        await using var db  = CreateDb();
        var empId           = Guid.NewGuid();
        var endpoint        = "https://push.example.com/abc";

        // Pre-seed the subscription
        db.PushSubscriptions.Add(E.PushSubscription.Create(empId, endpoint, "key", "auth"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new SavePushSubscriptionCommandHandler(db);
        await handler.Handle(
            new SavePushSubscriptionCommand(empId, endpoint, "new-key", "new-auth"),
            CancellationToken.None);

        // Still only one subscription — no duplicate created
        Assert.Equal(1, await db.PushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task SavePushSubscription_DoesNotExceedCap()
    {
        await using var db  = CreateDb();
        var empId           = Guid.NewGuid();

        // Seed exactly at the cap (5 subscriptions)
        for (int i = 0; i < 5; i++)
            db.PushSubscriptions.Add(
                E.PushSubscription.Create(empId, $"https://push.example.com/sub{i}", $"key{i}", $"auth{i}"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new SavePushSubscriptionCommandHandler(db);
        await handler.Handle(
            new SavePushSubscriptionCommand(empId, "https://push.example.com/new", "newkey", "newauth"),
            CancellationToken.None);

        // Must still be capped at 5
        Assert.Equal(5, await db.PushSubscriptions.CountAsync());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // RemovePushSubscriptionCommand
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RemovePushSubscription_DoesNothing_WhenNotFound()
    {
        await using var db  = CreateDb();
        var handler         = new RemovePushSubscriptionCommandHandler(db);

        // Should not throw
        await handler.Handle(
            new RemovePushSubscriptionCommand(Guid.NewGuid(), "https://push.example.com/missing"),
            CancellationToken.None);
    }

    [Fact]
    public async Task RemovePushSubscription_Removes_WhenFound()
    {
        await using var db  = CreateDb();
        var empId           = Guid.NewGuid();
        var endpoint        = "https://push.example.com/abc";

        db.PushSubscriptions.Add(E.PushSubscription.Create(empId, endpoint, "key", "auth"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RemovePushSubscriptionCommandHandler(db);
        await handler.Handle(
            new RemovePushSubscriptionCommand(empId, endpoint),
            CancellationToken.None);

        Assert.Equal(0, await db.PushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task RemovePushSubscription_DoesNotRemove_WhenEndpointBelongsToDifferentEmployee()
    {
        await using var db = CreateDb();
        var empId1         = Guid.NewGuid();
        var empId2         = Guid.NewGuid();
        var endpoint       = "https://push.example.com/abc";

        db.PushSubscriptions.Add(E.PushSubscription.Create(empId1, endpoint, "key", "auth"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new RemovePushSubscriptionCommandHandler(db);
        // empId2 tries to remove empId1's subscription — different employee, so not found
        await handler.Handle(
            new RemovePushSubscriptionCommand(empId2, endpoint),
            CancellationToken.None);

        // empId1's subscription must remain
        Assert.Equal(1, await db.PushSubscriptions.CountAsync());
    }
}
