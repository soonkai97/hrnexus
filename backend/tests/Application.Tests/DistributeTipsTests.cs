using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Admin.Commands;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Application.Tests.Infrastructure;
using E = NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

/// <summary>
/// Handler-level tests for DistributeTipsCommand.
/// Guards input validation, duplicate prevention, staff selection,
/// equal-split, and by_hours split calculations.
/// </summary>
public class DistributeTipsTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class NullAuditService : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubDateTimeProvider : IDateTimeProvider
    {
        private readonly DateTime _now;
        public StubDateTimeProvider(DateTime? now = null) => _now = now ?? DateTime.UtcNow;
        public DateTime UtcNow => _now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(_now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => _now;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static DistributeTipsHandler MakeHandler(TestDbContext db, DateTime? now = null) =>
        new(db, new StubDateTimeProvider(now), new NullAuditService());

    private static E.Staff MakeStaff(string id = "S001", string first = "Alice")
        => E.Staff.Create(id, first, "Wong", $"{first.ToLower()}@test.com", "hash",
            new List<string> { "BARTENDER" });

    private static DistributeTipsCommand MakeCmd(
        Guid locId, DateTime shiftDate, decimal amount = 1000m, string method = "equal") =>
        new(locId, "Main Hall", shiftDate, amount, method, Guid.NewGuid(), "Admin");

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DistributeTips_ThrowsArgument_WhenAmountIsZero()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db);
        var cmd = MakeCmd(Guid.NewGuid(), DateTime.UtcNow, amount: 0m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task DistributeTips_ThrowsArgument_WhenAmountIsNegative()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db);
        var cmd = MakeCmd(Guid.NewGuid(), DateTime.UtcNow, amount: -100m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task DistributeTips_ThrowsArgument_WhenAmountExceedsMaxLimit()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db);
        var cmd = MakeCmd(Guid.NewGuid(), DateTime.UtcNow, amount: 50_001m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    // ── Duplicate prevention ──────────────────────────────────────────────────

    [Fact]
    public async Task DistributeTips_ThrowsInvalidOperation_WhenAlreadyDistributedForSameLocationAndDate()
    {
        await using var db = CreateDb();
        var locId = Guid.NewGuid();
        var shiftDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        // Seed an existing pool for same location and date
        var pool = E.TipsPool.Create(locId, "Main Hall", shiftDate, 500m, "equal");
        db.TipsPools.Add(pool);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeHandler(db);
        var cmd = MakeCmd(locId, shiftDate);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    // ── No active staff ───────────────────────────────────────────────────────

    [Fact]
    public async Task DistributeTips_ThrowsInvalidOperation_WhenNoActiveStaffForLocation()
    {
        await using var db = CreateDb();
        var locId = Guid.NewGuid();
        var shiftDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var workDate = DateOnly.FromDateTime(shiftDate);

        var handler = MakeHandler(db);
        var cmd = MakeCmd(locId, shiftDate);

        // No WorkLogs seeded — no active staff
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    // ── Equal split ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DistributeTips_EqualSplit_DividesEvenlyAmong2Staff()
    {
        await using var db = CreateDb();
        var locId = Guid.NewGuid();
        var locIdStr = locId.ToString();
        var shiftDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var workDate = DateOnly.FromDateTime(shiftDate);

        var staff1 = MakeStaff("S001", "Alice");
        var staff2 = MakeStaff("S002", "Bob");

        // Active (EndTime=null) work logs at the location
        var log1 = E.WorkLog.StartLive(staff1.Id, "BARTENDER", locIdStr, "Main Hall",
            workDate, "20:00", 20m);
        var log2 = E.WorkLog.StartLive(staff2.Id, "BARTENDER", locIdStr, "Main Hall",
            workDate, "20:00", 20m);

        db.Staff.Add(staff1);
        db.Staff.Add(staff2);
        db.WorkLogs.Add(log1);
        db.WorkLogs.Add(log2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeHandler(db, now: shiftDate.AddHours(3));
        var cmd = MakeCmd(locId, shiftDate, amount: 1000m, method: "equal");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(2, result.StaffCount);
        Assert.Equal(2, result.Allocations.Count);
        Assert.Equal(1000m, result.Allocations.Sum(a => a.Amount));
        // Each should get 500
        Assert.All(result.Allocations, a => Assert.Equal(500m, a.Amount));
    }

    [Fact]
    public async Task DistributeTips_EqualSplit_HandlesRemainderOnLastStaff()
    {
        await using var db = CreateDb();
        var locId = Guid.NewGuid();
        var locIdStr = locId.ToString();
        var shiftDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var workDate = DateOnly.FromDateTime(shiftDate);

        var staff1 = MakeStaff("S001", "Alice");
        var staff2 = MakeStaff("S002", "Bob");
        var staff3 = MakeStaff("S003", "Carol");

        db.Staff.Add(staff1);
        db.Staff.Add(staff2);
        db.Staff.Add(staff3);
        db.WorkLogs.Add(E.WorkLog.StartLive(staff1.Id, "B", locIdStr, "Main", workDate, "20:00", 0));
        db.WorkLogs.Add(E.WorkLog.StartLive(staff2.Id, "B", locIdStr, "Main", workDate, "20:00", 0));
        db.WorkLogs.Add(E.WorkLog.StartLive(staff3.Id, "B", locIdStr, "Main", workDate, "20:00", 0));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeHandler(db, now: shiftDate.AddHours(3));
        // 100 / 3 = 33.33 each; last gets the remainder
        var cmd = MakeCmd(locId, shiftDate, amount: 100m, method: "equal");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(100m, result.Allocations.Sum(a => a.Amount));
        Assert.Equal(3, result.Allocations.Count);
    }

    // ── By-hours split ────────────────────────────────────────────────────────

    [Fact]
    public async Task DistributeTips_ByHoursSplit_DistributesProportionally()
    {
        await using var db = CreateDb();
        var locId = Guid.NewGuid();
        var locIdStr = locId.ToString();
        var shiftDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var workDate = DateOnly.FromDateTime(shiftDate);

        // staff1 started 2 hours ago, staff2 started 1 hour ago
        var now = shiftDate.AddHours(3);
        var staff1 = MakeStaff("S001", "Alice");
        var staff2 = MakeStaff("S002", "Bob");

        db.Staff.Add(staff1);
        db.Staff.Add(staff2);
        db.WorkLogs.Add(E.WorkLog.StartLive(staff1.Id, "B", locIdStr, "Main", workDate, "01:00", 0));
        db.WorkLogs.Add(E.WorkLog.StartLive(staff2.Id, "B", locIdStr, "Main", workDate, "02:00", 0));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeHandler(db, now: now);
        var cmd = MakeCmd(locId, shiftDate, amount: 300m, method: "by_hours");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(300m, result.Allocations.Sum(a => a.Amount));
        Assert.Equal(2, result.Allocations.Count);
        // staff1 worked 2h, staff2 worked 1h → staff1 gets 200, staff2 gets 100
        var alice = result.Allocations.First(a => a.EmployeeId == staff1.Id);
        var bob   = result.Allocations.First(a => a.EmployeeId == staff2.Id);
        Assert.True(alice.Amount > bob.Amount, "Staff who worked longer should get more tips.");
    }

    // ── Excluded staff (already clocked out) ─────────────────────────────────

    [Fact]
    public async Task DistributeTips_ReportsExcludedStaff_WhoAlreadyClockedOut()
    {
        await using var db = CreateDb();
        var locId = Guid.NewGuid();
        var locIdStr = locId.ToString();
        var shiftDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var workDate = DateOnly.FromDateTime(shiftDate);

        var activeStaff  = MakeStaff("S001", "Alice");
        var excludedStaff = MakeStaff("S002", "Bob");

        // Active log (EndTime=null) for Alice
        var activeLog = E.WorkLog.StartLive(activeStaff.Id, "B", locIdStr, "Main", workDate, "20:00", 0);
        // Completed log (EndTime set) for Bob
        var completedLog = E.WorkLog.StartLive(excludedStaff.Id, "B", locIdStr, "Main", workDate, "18:00", 0);
        completedLog.End("21:00");

        db.Staff.Add(activeStaff);
        db.Staff.Add(excludedStaff);
        db.WorkLogs.Add(activeLog);
        db.WorkLogs.Add(completedLog);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeHandler(db, now: shiftDate.AddHours(3));
        var cmd = MakeCmd(locId, shiftDate, amount: 1000m, method: "equal");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(1, result.StaffCount);  // only Alice is active
        Assert.Single(result.ExcludedStaff); // Bob is excluded
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DistributeTips_PersistsTipsPoolAndAllocations()
    {
        await using var db = CreateDb();
        var locId = Guid.NewGuid();
        var locIdStr = locId.ToString();
        var shiftDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var workDate = DateOnly.FromDateTime(shiftDate);

        var staff = MakeStaff("S001", "Alice");
        db.Staff.Add(staff);
        db.WorkLogs.Add(E.WorkLog.StartLive(staff.Id, "B", locIdStr, "Main", workDate, "20:00", 0));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = MakeHandler(db, now: shiftDate.AddHours(3));
        await handler.Handle(MakeCmd(locId, shiftDate, amount: 500m), CancellationToken.None);

        Assert.Equal(1, await db.TipsPools.CountAsync());
        Assert.Equal(1, await db.TipsAllocations.CountAsync());
    }
}
