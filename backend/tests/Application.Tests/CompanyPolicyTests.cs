using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Policy;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Policy;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7m-a: the working week, hours per day, leave limits and carry-over rule are company settings.
/// Defaults reproduce Phase 7l exactly; a company can change them without a release.
/// </summary>
public class CompanyPolicyTests
{
    private sealed class NullHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullPush : IPushNotificationService
    {
        public Task SendAsync(Guid id, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendToManyAsync(IEnumerable<Guid> ids, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Clock(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow => now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => now;
    }

    private static readonly DateTime Today = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);   // a Friday

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<E.Staff> AddStaff(TestDbContext db)
    {
        var roleId = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        var staff = E.Staff.Create("Z1", "Pol", "Icy", "z1@t.com", "hash", new List<Guid> { roleId });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    private static UpdateCompanyPolicyCommand Update(int[]? days = null, decimal hours = 8m, int notice = 3, int maxDays = 30,
        string mode = "capped", decimal cap = 14m, decimal divisor = 26m, int? expectedVersion = null) =>
        new(days ?? [1, 2, 3, 4, 5], hours, notice, maxDays, mode, cap, divisor, expectedVersion);

    private static UpsertLeaveCommandHandler UpsertHandler(TestDbContext db) =>
        new(db, new NullHub(), new NullPush(), new NullAudit(), new Clock(Today));

    // ── WorkSchedule ──────────────────────────────────────────────────────────

    [Fact]
    public void WorkSchedule_Default_IsMondayToFridayEightHours()
    {
        var s = WorkSchedule.Default;
        Assert.Equal(8m, s.WorkdayHours);
        Assert.True(s.IsWorkingDay(DayOfWeek.Monday));
        Assert.True(s.IsWorkingDay(DayOfWeek.Friday));
        Assert.False(s.IsWorkingDay(DayOfWeek.Saturday));
        Assert.False(s.IsWorkingDay(DayOfWeek.Sunday));
    }

    [Fact]
    public void WorkSchedule_FromIsoDays_MapsEachWeekdayIncludingSunday()
    {
        var s = WorkSchedule.FromIsoDays([1, 6, 7], 8m);

        Assert.True(s.IsWorkingDay(DayOfWeek.Monday));
        Assert.True(s.IsWorkingDay(DayOfWeek.Saturday));
        Assert.True(s.IsWorkingDay(DayOfWeek.Sunday));
        Assert.False(s.IsWorkingDay(DayOfWeek.Wednesday));
        Assert.Equal([1, 6, 7], s.IsoDays);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithNoSavedPolicy_TheDefaultsApply()
    {
        await using var db = CreateDb();

        var policy = await CompanyPolicyReader.GetAsync(db, CancellationToken.None);

        Assert.Equal(WorkSchedule.Default, policy.Schedule);
        Assert.Equal(3, policy.AnnualLeaveAdvanceDays);
        Assert.Equal(30, policy.MaxLeaveDays);
        Assert.Equal("capped", policy.CarryOverMode);
        Assert.Equal(14m, policy.CarryOverMaxHours);
        Assert.Equal(26m, policy.UnpaidLeaveDivisor);
    }

    // ── Saving ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_CreatesTheRowFirstTime_ThenBumpsTheVersion()
    {
        await using var db = CreateDb();
        var handler = new UpdateCompanyPolicyCommandHandler(db, new NullAudit());

        var v1 = await handler.Handle(Update(days: [1, 2, 3, 4, 5, 6]), CancellationToken.None);
        var v2 = await handler.Handle(Update(days: [1, 2, 3, 4, 5, 6], expectedVersion: v1), CancellationToken.None);

        Assert.Equal(1, v1);
        Assert.Equal(2, v2);
        var saved = await CompanyPolicyReader.GetAsync(db, CancellationToken.None);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, saved.Schedule.IsoDays);
    }

    [Fact]
    public async Task Save_WithAStaleVersion_IsRefused_SoTwoAdminsNeverOverwriteEachOther()
    {
        await using var db = CreateDb();
        var handler = new UpdateCompanyPolicyCommandHandler(db, new NullAudit());
        var v1 = await handler.Handle(Update(), CancellationToken.None);
        await handler.Handle(Update(notice: 5, expectedVersion: v1), CancellationToken.None);   // someone else saved

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(Update(notice: 7, expectedVersion: v1), CancellationToken.None));

        Assert.Equal(5, (await CompanyPolicyReader.GetAsync(db, CancellationToken.None)).AnnualLeaveAdvanceDays);
    }

    [Theory]
    [InlineData(new int[0], 8, 3, 30, "capped", 14, 26)]          // no working day
    [InlineData(new[] { 1, 8 }, 8, 3, 30, "capped", 14, 26)]      // not a weekday
    [InlineData(new[] { 1, 1 }, 8, 3, 30, "capped", 14, 26)]      // duplicate day
    [InlineData(new[] { 1, 2 }, 0, 3, 30, "capped", 14, 26)]      // zero-hour day
    [InlineData(new[] { 1, 2 }, 25, 3, 30, "capped", 14, 26)]     // more than 24h
    [InlineData(new[] { 1, 2 }, 8, -1, 30, "capped", 14, 26)]     // negative notice
    [InlineData(new[] { 1, 2 }, 8, 61, 30, "capped", 14, 26)]     // notice too long
    [InlineData(new[] { 1, 2 }, 8, 3, 0, "capped", 14, 26)]       // max days below 1
    [InlineData(new[] { 1, 2 }, 8, 3, 367, "capped", 14, 26)]     // max days above a year
    [InlineData(new[] { 1, 2 }, 8, 3, 30, "forever", 14, 26)]     // unknown carry-over mode
    [InlineData(new[] { 1, 2 }, 8, 3, 30, "capped", -1, 26)]      // negative cap
    [InlineData(new[] { 1, 2 }, 8, 3, 30, "capped", 14, 19)]      // divisor too low
    [InlineData(new[] { 1, 2 }, 8, 3, 30, "capped", 14, 32)]      // divisor too high
    public async Task Save_RejectsInvalidValues(int[] days, int hours, int notice, int maxDays, string mode, int cap, int divisor)
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new UpdateCompanyPolicyCommandHandler(db, new NullAudit())
                .Handle(Update(days, hours, notice, maxDays, mode, cap, divisor), CancellationToken.None));

        Assert.Empty(await db.CompanyPolicies.ToListAsync());
    }

    // ── The policy drives leave ───────────────────────────────────────────────

    [Fact]
    public async Task AMondayToSaturdayCompany_CanBookASaturday_AMondayToFridayCompanyCannot()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var saturday = D(2026, 6, 6);
        UpsertLeaveCommand Request() => new(null, staff.Id, "unpaid", saturday, saturday, null, null, null, null, "pending");

        await Assert.ThrowsAsync<ArgumentException>(() => UpsertHandler(db).Handle(Request(), CancellationToken.None));   // default Mon-Fri

        await new UpdateCompanyPolicyCommandHandler(db, new NullAudit()).Handle(Update(days: [1, 2, 3, 4, 5, 6]), CancellationToken.None);
        var id = await UpsertHandler(db).Handle(Request(), CancellationToken.None);

        Assert.Equal(8m, (await db.LeaveRequests.FirstAsync(l => l.Id == id)).Hours);
    }

    [Fact]
    public async Task HoursPerDay_ComesFromThePolicy()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await new UpdateCompanyPolicyCommandHandler(db, new NullAudit()).Handle(Update(hours: 6m), CancellationToken.None);

        // Mon-Wed, three working days of six hours.
        var id = await UpsertHandler(db).Handle(
            new UpsertLeaveCommand(null, staff.Id, "unpaid", D(2026, 6, 1), D(2026, 6, 3), null, null, null, null, "pending"), CancellationToken.None);

        Assert.Equal(18m, (await db.LeaveRequests.FirstAsync(l => l.Id == id)).Hours);
    }

    [Fact]
    public async Task AnnualLeaveNotice_ComesFromThePolicy()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 200m));
        await db.SaveChangesAsync();
        var inTenDays = Today.AddDays(10);   // Mon 11 May 2026
        UpsertLeaveCommand Request() => new(null, staff.Id, "annual", inTenDays, inTenDays, null, null, null, null, "pending");

        await new UpdateCompanyPolicyCommandHandler(db, new NullAudit()).Handle(Update(notice: 14), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => UpsertHandler(db).Handle(Request(), CancellationToken.None));

        await new UpdateCompanyPolicyCommandHandler(db, new NullAudit()).Handle(Update(notice: 0, expectedVersion: 1), CancellationToken.None);
        Assert.NotEqual(Guid.Empty, await UpsertHandler(db).Handle(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task LongestRequest_ComesFromThePolicy()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await new UpdateCompanyPolicyCommandHandler(db, new NullAudit()).Handle(Update(maxDays: 5), CancellationToken.None);

        // Mon 1 Jun to Mon 8 Jun is eight calendar days.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UpsertHandler(db).Handle(new UpsertLeaveCommand(null, staff.Id, "unpaid", D(2026, 6, 1), D(2026, 6, 8), null, null, null, null, "pending"), CancellationToken.None));
    }

    // ── Carry-over follows the policy ─────────────────────────────────────────

    private static async Task<(TestDbContext Db, E.Staff Staff)> CarryOverSetup(string mode, decimal cap)
    {
        var db = CreateDb();
        var staff = await AddStaff(db);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 100m));   // nothing used
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 100m));
        await db.SaveChangesAsync();
        await new UpdateCompanyPolicyCommandHandler(db, new NullAudit()).Handle(Update(mode: mode, cap: cap), CancellationToken.None);
        return (db, staff);
    }

    [Theory]
    [InlineData("none", 14, 0, 0)]          // unused leave lapses: nothing carried, nobody counted
    [InlineData("capped", 14, 14, 1)]       // up to the cap
    [InlineData("capped", 500, 100, 1)]     // the cap is higher than what is left
    [InlineData("unlimited", 14, 100, 1)]   // cumulative: everything that is left
    public async Task CarryOver_FollowsTheCompanyRule(string mode, int cap, int expectedCarry, int expectedCount)
    {
        var (db, staff) = await CarryOverSetup(mode, cap);
        await using var _ = db;

        var count = await new CarryOverLeaveEntitlementCommandHandler(db)
            .Handle(new CarryOverLeaveEntitlementCommand(2025, 2026), CancellationToken.None);

        Assert.Equal(expectedCount, count);
        db.ChangeTracker.Clear();
        var to = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id && e.Year == 2026);
        Assert.Equal(expectedCarry, to.CarryOverHours);
        Assert.Equal(100m, to.AnnualHours);   // the base entitlement is never touched
    }
}
