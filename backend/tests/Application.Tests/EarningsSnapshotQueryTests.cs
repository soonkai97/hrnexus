using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// GetEarningsSnapshotQuery — live per-staff "earned today" + "earned this month".
///
/// WHY these tests matter:
///  - The owner reads these figures to know payout-so-far at a glance; an off-by-a-shift
///    error misstates what a staff member is owed.
///  - "Today" must be a strict subset of "this month", anchored to the venue's local date.
///  - Only COMPLETED shifts are payable — an open (still-clocked-in) shift must not inflate
///    the numbers, and shifts from a prior month must be excluded entirely.
/// </summary>
public class EarningsSnapshotQueryTests
{
    private sealed class FixedDateTimeProvider(DateTime utcNow, TimeZoneInfo? tz = null) : IDateTimeProvider
    {
        public DateTime     UtcNow        => utcNow;
        public DateOnly     TodayUtc      => DateOnly.FromDateTime(utcNow);
        public TimeZoneInfo VenueTimeZone => tz ?? TimeZoneInfo.Utc;
        public DateTime     LocalNow      => TimeZoneInfo.ConvertTimeFromUtc(utcNow, VenueTimeZone);
    }

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static E.Staff MakeStaff(string staffId, string first) =>
        E.Staff.Create(staffId, first, "Wong", $"{staffId}@test.com", "hash", new List<string> { "BARTENDER" });

    // Mid-month reference so "today - 3 days" stays inside the same month.
    private static readonly DateTime UtcRef = new(2025, 6, 15, 14, 0, 0, DateTimeKind.Utc);

    private static E.WorkLog CompletedShift(Guid empId, DateOnly date, string start, string end, decimal rate)
    {
        var log = E.WorkLog.StartLive(empId, "BARTENDER", "loc1", "Main", date, start, rate);
        log.End(end);   // same-day end (end > start)
        return log;
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoCompletedShifts()
    {
        await using var db = CreateDb();
        var handler = new GetEarningsSnapshotQueryHandler(db, new FixedDateTimeProvider(UtcRef));

        var result = await handler.Handle(new GetEarningsSnapshotQuery(), CancellationToken.None);

        Assert.Empty(result.Staff);
        Assert.Equal(0m, result.TotalToday);
        Assert.Equal(0m, result.TotalThisMonth);
        Assert.Equal("2025-06-15", result.Date);
        Assert.Equal("2025-06", result.Month);
    }

    [Fact]
    public async Task SplitsTodayVsMonth_AndExcludesOpenAndPriorMonthShifts()
    {
        await using var db = CreateDb();
        var alice = MakeStaff("S001", "Alice");
        db.Staff.Add(alice);

        var today      = DateOnly.FromDateTime(UtcRef);          // 2025-06-15
        var earlier    = today.AddDays(-3);                      // 2025-06-12 (same month)
        var lastMonth  = new DateOnly(2025, 5, 30);

        // Today: 8h × 20 = 160 → counts toward today AND month
        db.WorkLogs.Add(CompletedShift(alice.Id, today, "09:00", "17:00", 20m));
        // Earlier this month: 4h × 20 = 80 → month only
        db.WorkLogs.Add(CompletedShift(alice.Id, earlier, "09:00", "13:00", 20m));
        // Last month: excluded entirely
        db.WorkLogs.Add(CompletedShift(alice.Id, lastMonth, "09:00", "17:00", 20m));
        // Open shift today (not ended): not payable → must not inflate today's figure
        db.WorkLogs.Add(E.WorkLog.StartLive(alice.Id, "BARTENDER", "loc1", "Main", today, "20:00", 20m));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetEarningsSnapshotQueryHandler(db, new FixedDateTimeProvider(UtcRef));
        var result  = await handler.Handle(new GetEarningsSnapshotQuery(), CancellationToken.None);

        var row = Assert.Single(result.Staff);
        Assert.Equal("S001", row.StaffId);
        Assert.Equal("Alice Wong", row.Name);
        Assert.Equal(160m, row.EarnedToday);
        Assert.Equal(240m, row.EarnedThisMonth);  // 160 + 80, last-month shift excluded
        Assert.Equal(160m, result.TotalToday);
        Assert.Equal(240m, result.TotalThisMonth);
    }

    [Fact]
    public async Task AggregatesAcrossStaff_AndOrdersByTodayEarningsDesc()
    {
        await using var db = CreateDb();
        var alice = MakeStaff("S001", "Alice");
        var bob   = MakeStaff("S002", "Bob");
        db.Staff.Add(alice);
        db.Staff.Add(bob);

        var today = DateOnly.FromDateTime(UtcRef);
        // Alice today: 2h × 20 = 40
        db.WorkLogs.Add(CompletedShift(alice.Id, today, "09:00", "11:00", 20m));
        // Bob today: 5h × 20 = 100  → should sort ABOVE Alice
        db.WorkLogs.Add(CompletedShift(bob.Id, today, "09:00", "14:00", 20m));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetEarningsSnapshotQueryHandler(db, new FixedDateTimeProvider(UtcRef));
        var result  = await handler.Handle(new GetEarningsSnapshotQuery(), CancellationToken.None);

        Assert.Equal(2, result.Staff.Count);
        Assert.Equal("S002", result.Staff[0].StaffId);   // Bob first (earned more today)
        Assert.Equal("S001", result.Staff[1].StaffId);
        Assert.Equal(140m, result.TotalToday);
        Assert.Equal(140m, result.TotalThisMonth);
    }
}
