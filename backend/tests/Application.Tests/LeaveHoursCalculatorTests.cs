using System.Text.Json;
using HRNexus.Application.Leave;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7l (D4/D5): leave hours come from working days (Mon-Fri, 8h), not clock time, and a leave
/// that crosses 31 Dec / 1 Jan is split by the days that fall in each year. The vectors live in
/// tests/fixtures/leave-hours.json and are asserted by the frontend spec too, so both stacks
/// show and charge the same number.
/// </summary>
public class LeaveHoursCalculatorTests
{
    private static JsonElement Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tests", "fixtures", "leave-hours.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "leave-hours.json"))).RootElement;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime Date(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

    public static TheoryData<string> CalculateCases()
    {
        var data = new TheoryData<string>();
        foreach (var c in Fixture().GetProperty("calculate").EnumerateArray()) data.Add(c.GetProperty("name").GetString()!);
        return data;
    }

    public static TheoryData<string> InYearCases()
    {
        var data = new TheoryData<string>();
        foreach (var c in Fixture().GetProperty("hoursInYear").EnumerateArray()) data.Add(c.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(CalculateCases))]
    public void Calculate_FollowsTheSharedVectors(string name)
    {
        var c = Fixture().GetProperty("calculate").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var start = Date(Str(c, "start")!);
        var end = Date(Str(c, "end")!);

        if (c.TryGetProperty("error", out _))
        {
            Assert.Throws<ArgumentException>(() => LeaveHoursCalculator.Calculate(start, end, Str(c, "startTime"), Str(c, "endTime")));
            return;
        }

        Assert.Equal(c.GetProperty("hours").GetDecimal(),
            LeaveHoursCalculator.Calculate(start, end, Str(c, "startTime"), Str(c, "endTime")));
    }

    [Theory]
    [MemberData(nameof(InYearCases))]
    public void HoursInYear_FollowsTheSharedVectors(string name)
    {
        var c = Fixture().GetProperty("hoursInYear").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var start = Date(Str(c, "start")!);
        var end = Date(Str(c, "end")!);
        var total = LeaveHoursCalculator.Calculate(start, end, Str(c, "startTime"), Str(c, "endTime"));

        Assert.Equal(c.GetProperty("hours").GetDecimal(),
            LeaveHoursCalculator.HoursInYear(start, end, total, c.GetProperty("year").GetInt32()));
    }

    [Fact]
    public void HoursInYear_ForALegacyRowWithoutStoredHours_FallsBackToTheWorkingDayRule()
    {
        // Rows created before hours were stored have Hours = null; they must still be chargeable.
        Assert.Equal(40m, LeaveHoursCalculator.HoursInYear(Date("2026-06-01"), Date("2026-06-05"), null, 2026));
    }

    [Fact]
    public void WorkdayHours_IsEight_BecauseTheVenueWorksAnEightHourDay()
    {
        Assert.Equal(8m, HRNexus.Domain.Constants.AppConstants.Leave.WorkdayHours);
    }
}
