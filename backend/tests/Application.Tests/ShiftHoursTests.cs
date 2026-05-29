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
}
