using HRNexus.Application.Common;
using Xunit;

namespace HRNexus.Application.Tests;

public class ShiftHoursTests
{
    [Theory]
    [InlineData("09:00", "17:00", 8.0)]
    [InlineData("09:00", "09:00", 24.0)]   // full 24-hour shift
    [InlineData("22:00", "02:00", 4.0)]    // midnight crossing
    [InlineData("23:00", "06:00", 7.0)]    // overnight shift
    [InlineData("18:00", "00:00", 6.0)]    // ends exactly at midnight
    [InlineData("00:00", "04:30", 4.5)]    // starts at midnight
    public void CalcHours_ReturnsCorrectDuration(string start, string end, double expected)
    {
        var result = ShiftHours.CalcHours(start, end);
        Assert.Equal((decimal)expected, result);
    }

    // ── FloorToHalf: hourly billing counts only completed 30-min blocks ────────

    [Theory]
    [InlineData(1.75, 1.5)]    // 1h45m → 1.5
    [InlineData(1.50, 1.5)]    // exact half-hour stays
    [InlineData(1.49, 1.0)]    // 1h29.4m → 1.0
    [InlineData(1.00, 1.0)]    // exact hour stays
    [InlineData(0.40, 0.0)]    // under 30 min → 0
    [InlineData(2.00, 2.0)]    // whole hour stays
    public void FloorToHalf_RoundsDownToNearestHalfHour(decimal input, decimal expected)
    {
        Assert.Equal(expected, ShiftHours.FloorToHalf(input));
    }

    // ── BillableHours: floor for hourly, exact for fixed ──────────────────────

    [Fact]
    public void BillableHours_Hourly_FloorsToHalf()
    {
        Assert.Equal(1.5m, ShiftHours.BillableHours("hourly", 1.75m));
    }

    [Fact]
    public void BillableHours_Fixed_KeepsExactHours()
    {
        // A fixed package shows the real time worked; only its pay is flat (handled by ShiftPay).
        Assert.Equal(1.75m, ShiftHours.BillableHours("fixed", 1.75m));
    }
}
