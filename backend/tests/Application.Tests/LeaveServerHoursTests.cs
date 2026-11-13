using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7l (D4/D5): the server owns leave hours (the client value is ignored) and a leave that
/// crosses a year boundary is charged to each year's balance by the days that fall in that year.
/// Dates are fixed (2026-06-01 is a Monday; 2026-12-28 to 2027-01-01 is Mon-Fri across the new
/// year) so the tests never depend on the weekday they run on.
/// </summary>
public class LeaveServerHoursTests
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

    private sealed class Clock : IDateTimeProvider
    {
        private static readonly DateTime Now = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(Now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => Now;
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<E.Staff> AddStaff(TestDbContext db)
    {
        var roleId = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        var staff = E.Staff.Create("H1", "Hours", "Test", "h1@t.com", "hash", new List<Guid> { roleId });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    private static UpsertLeaveCommandHandler Handler(TestDbContext db) =>
        new(db, new NullHub(), new NullPush(), new NullAudit(), new Clock());

    private static UpsertLeaveCommand Sick(Guid staffId, DateTime start, DateTime end, decimal? clientHours = 999m,
        string? startTime = null, string? endTime = null) =>
        new(null, staffId, "sick", start, end, startTime, endTime, clientHours, null, "pending");

    private static async Task Entitle(TestDbContext db, Guid staffId, int year, decimal sick)
    {
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staffId, year, annualHours: 0m, sickHours: sick));
        await db.SaveChangesAsync();
    }

    // ── D4: the server owns the hours ─────────────────────────────────────────

    [Fact]
    public async Task Apply_StoresServerCalculatedHours_AndIgnoresTheClientValue()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 200m);

        // Mon-Wed: 3 working days = 24h. The client claims 999h (and a clock-time reading would be 48h+).
        var id = await Handler(db).Handle(Sick(staff.Id, D(2026, 6, 1), D(2026, 6, 3)), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(24m, (await db.LeaveRequests.FirstAsync(l => l.Id == id)).Hours);
    }

    [Fact]
    public async Task Apply_DoesNotChargeTheWeekend()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 200m);

        var id = await Handler(db).Handle(Sick(staff.Id, D(2026, 6, 5), D(2026, 6, 8)), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(16m, (await db.LeaveRequests.FirstAsync(l => l.Id == id)).Hours);  // Fri + Mon only
    }

    [Fact]
    public async Task Apply_ForAWeekendOnly_IsRejected()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 200m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Sick(staff.Id, D(2026, 6, 6), D(2026, 6, 7)), CancellationToken.None));
    }

    [Fact]
    public async Task Apply_ASingleDayWithTimes_ChargesThoseHours()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 200m);

        var id = await Handler(db).Handle(Sick(staff.Id, D(2026, 6, 1), D(2026, 6, 1), startTime: "09:00", endTime: "13:00"), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(4m, (await db.LeaveRequests.FirstAsync(l => l.Id == id)).Hours);
    }

    [Fact]
    public async Task Apply_WithoutAnyClientHours_Succeeds()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 200m);

        var id = await Handler(db).Handle(Sick(staff.Id, D(2026, 6, 1), D(2026, 6, 1), clientHours: null), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Apply_ForMoreThanThirtyDays_IsRejected()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 2000m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Sick(staff.Id, D(2026, 6, 1), D(2026, 7, 15)), CancellationToken.None));
    }

    // ── D5: a leave across New Year is charged to each year ───────────────────

    [Fact]
    public async Task Apply_AcrossNewYear_NeedsAnEntitlementForBothYears()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 100m);   // 2027 has none

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(Sick(staff.Id, D(2026, 12, 28), D(2027, 1, 1)), CancellationToken.None));

        Assert.Contains("2027", ex.Message);
    }

    [Fact]
    public async Task Apply_AcrossNewYear_ChecksEachYearAgainstItsOwnShare()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 24m);    // needs 32h (Mon-Thu) in 2026
        await Entitle(db, staff.Id, 2027, 100m);   // needs 8h (Fri 1 Jan) in 2027

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(Sick(staff.Id, D(2026, 12, 28), D(2027, 1, 1)), CancellationToken.None));

        Assert.Contains("2026", ex.Message);
    }

    [Fact]
    public async Task Apply_AcrossNewYear_Succeeds_WhenBothYearsHaveRoom()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 32m);
        await Entitle(db, staff.Id, 2027, 8m);

        var id = await Handler(db).Handle(Sick(staff.Id, D(2026, 12, 28), D(2027, 1, 1)), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Balance_SplitsACrossYearLeaveByYear()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        await Entitle(db, staff.Id, 2026, 100m);
        await Entitle(db, staff.Id, 2027, 100m);
        await Handler(db).Handle(Sick(staff.Id, D(2026, 12, 28), D(2027, 1, 1)), CancellationToken.None);
        var query = new GetLeaveBalanceQueryHandler(db, new Clock());

        var y2026 = await query.Handle(new GetLeaveBalanceQuery(staff.Id, 2026), CancellationToken.None);
        var y2027 = await query.Handle(new GetLeaveBalanceQuery(staff.Id, 2027), CancellationToken.None);

        Assert.Equal(32m, y2026.SickUsed);
        Assert.Equal(8m, y2027.SickUsed);
    }

    [Fact]
    public async Task CarryOver_CountsOnlyTheDaysUsedInTheClosingYear()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 100m, sickHours: 0m));
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2027, annualHours: 100m, sickHours: 0m));
        // Annual leave Mon 28 Dec 2026 to Fri 1 Jan 2027: 32h used in 2026, 8h in 2027.
        var leave = E.LeaveRequest.Create(staff.Id, "annual", D(2026, 12, 28), D(2027, 1, 1), null, null, 40m, null);
        leave.Update("annual", D(2026, 12, 28), D(2027, 1, 1), null, null, 40m, null, "approved");
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Cumulative leave: everything left carries forward, so the carried figure shows what counted as used.
        await new HRNexus.Application.Policy.UpdateCompanyPolicyCommandHandler(db, new NullAudit())
            .Handle(new HRNexus.Application.Policy.UpdateCompanyPolicyCommand([1, 2, 3, 4, 5], 8m, 3, 30, "unlimited", 14m, 26m, null), CancellationToken.None);
        var moved = await new CarryOverLeaveEntitlementCommandHandler(db)
            .Handle(new CarryOverLeaveEntitlementCommand(2026, 2027), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(1, moved);
        // 100 entitlement - 32 used in 2026 = 68 carried over (not 100 - 40 = 60).
        Assert.Equal(68m, (await db.LeaveEntitlements.FirstAsync(e => e.Year == 2027)).CarryOverHours);
    }
}
