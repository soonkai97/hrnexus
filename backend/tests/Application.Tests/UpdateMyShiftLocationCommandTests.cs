using Microsoft.EntityFrameworkCore;
using HRNexus.Application.WorkLog.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// "Update location" lets a clocked-in staff member push a fresh GPS fix during the shift so the
/// admin dashboard shows where they currently are. WHY these matter:
///  - it must write to the caller's OWN active shift (never a finished one, never someone else's);
///  - with no active shift there is nothing to update — that must fail loudly, not silently no-op.
/// </summary>
public class UpdateMyShiftLocationCommandTests
{
    private sealed class FixedClock(DateTime now) : IDateTimeProvider
    {
        public DateTime     UtcNow        => now;
        public DateOnly     TodayUtc      => DateOnly.FromDateTime(now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Local;
        public DateTime     LocalNow      => now;
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static E.WorkLog ActiveShift(Guid empId) =>
        E.WorkLog.StartLive(empId, "BARTENDER", "loc1", "MAIN BAR", new DateOnly(2026, 6, 30), "09:00", 20m);

    [Fact]
    public async Task Records_the_fresh_fix_and_timestamp_on_the_active_shift()
    {
        var emp = Guid.NewGuid();
        await using var db = CreateDb();
        db.WorkLogs.Add(ActiveShift(emp));
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTime(2026, 6, 30, 15, 45, 0));
        var handler = new UpdateMyShiftLocationCommandHandler(db, clock);

        await handler.Handle(new UpdateMyShiftLocationCommand(emp, 3.1390, 101.6869), default);

        var log = await db.WorkLogs.SingleAsync();
        Assert.Equal(3.1390, log.LastLatitude);
        Assert.Equal(101.6869, log.LastLongitude);
        Assert.Equal(clock.LocalNow, log.LastLocationAt);
    }

    [Fact]
    public async Task Throws_when_the_caller_has_no_active_shift()
    {
        await using var db = CreateDb();
        var handler = new UpdateMyShiftLocationCommandHandler(db, new FixedClock(DateTime.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateMyShiftLocationCommand(Guid.NewGuid(), 1, 2), default));
    }

    [Fact]
    public async Task Will_not_update_an_already_ended_shift()
    {
        var emp = Guid.NewGuid();
        await using var db = CreateDb();
        var ended = ActiveShift(emp);
        ended.End("17:00");
        db.WorkLogs.Add(ended);
        await db.SaveChangesAsync();

        var handler = new UpdateMyShiftLocationCommandHandler(db, new FixedClock(DateTime.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateMyShiftLocationCommand(emp, 1, 2), default));
    }
}
