using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Pay-type pricing — a job is either HOURLY (pay = hours × rate) or FIXED (a flat amount per shift,
/// e.g. an overnight package) regardless of hours logged.
///
/// WHY these tests matter:
///  - The venue maps real rates this way (包夜 8h/12h/24h flat packages vs. hourly 跑台/写钟). If a
///    fixed shift were priced by hours it would be wildly wrong (e.g. a 12h/RM500 package billed as
///    12 × 500).
///  - The pay model is SNAPSHOTTED at clock-in, so re-typing the job later must not re-price history.
/// </summary>
public class ShiftPayTests
{
    private sealed class FixedDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime     UtcNow        => utcNow;
        public DateOnly     TodayUtc      => DateOnly.FromDateTime(utcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime     LocalNow      => utcNow;
    }

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    // ── ShiftPay.Calc (pure) ──────────────────────────────────────────────────

    [Fact]
    public void Calc_Hourly_MultipliesHoursByRate()
    {
        Assert.Equal(160m, ShiftPay.Calc(AppConstants.PayType.Hourly, 8m, 20m));
    }

    [Fact]
    public void Calc_Fixed_IgnoresHours_ReturnsFlatRate()
    {
        // 12-hour overnight package: flat RM500 no matter the hours logged.
        Assert.Equal(500m, ShiftPay.Calc(AppConstants.PayType.Fixed, 12m, 500m));
        Assert.Equal(500m, ShiftPay.Calc(AppConstants.PayType.Fixed, 0m,  500m));
    }

    [Fact]
    public void Calc_UnknownOrEmptyPayType_DefaultsToHourly()
    {
        // Defensive: a blank/legacy value must price the old (hourly) way, never as a flat fee.
        Assert.Equal(160m, ShiftPay.Calc("", 8m, 20m));
    }

    // ── Snapshot at clock-in flows through to earnings ────────────────────────

    [Fact]
    public async Task FixedShift_EarnsFlatRate_NotHoursTimesRate()
    {
        await using var db = CreateDb();
        var utcRef = new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        var packageRole = await RoleSeed.EnsureRoleAsync(db, "PACKAGE");
        var staff  = E.Staff.Create("S001", "Alice", "Wong", "a@test.com", "hash", new List<Guid> { packageRole });
        db.Staff.Add(staff);

        var today = DateOnly.FromDateTime(utcRef);
        // A 12-hour overnight FIXED package priced at RM500 flat. Hours-based pricing would give 6000.
        var log = E.WorkLog.StartLive(staff.Id, "PACKAGE", "loc1", "Main", today, "20:00", 500m,
            payType: AppConstants.PayType.Fixed);
        log.End("08:00");   // 12 hours, overnight
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetEarningsSnapshotQueryHandler(db, new FixedDateTimeProvider(utcRef));
        var result  = await handler.Handle(new GetEarningsSnapshotQuery(), CancellationToken.None);

        var row = Assert.Single(result.Staff);
        Assert.Equal(500m, row.EarnedToday);       // flat fee, NOT 12 × 500
        Assert.Equal(500m, row.EarnedThisMonth);
    }
}
