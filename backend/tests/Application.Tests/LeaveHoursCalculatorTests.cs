using System.Text.Json;
using HRNexus.Application.Leave;
using HRNexus.Domain.Policy;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7l/7m: leave hours come from the company's working days (default Mon-Fri, 8h), not clock time,
/// and a leave that crosses 31 Dec / 1 Jan is split by the days that fall in each year. The vectors live
/// in tests/fixtures/leave-hours.json and are asserted by the frontend spec too, so both stacks show and
/// charge the same number. The "scheduled" vectors cover other companies (Mon-Sat, a 4-day week, 6h days).
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

    private static WorkSchedule ScheduleOf(JsonElement c) =>
        c.TryGetProperty("schedule", out var s)
            ? WorkSchedule.FromIsoDays(s.GetProperty("days").EnumerateArray().Select(d => d.GetInt32()).ToArray(), s.GetProperty("hours").GetDecimal())
            : WorkSchedule.Default;

    private static TheoryData<string> Names(string section)
    {
        var data = new TheoryData<string>();
        foreach (var c in Fixture().GetProperty(section).EnumerateArray()) data.Add(c.GetProperty("name").GetString()!);
        return data;
    }

    public static TheoryData<string> CalculateCases() => Names("calculate");
    public static TheoryData<string> InYearCases() => Names("hoursInYear");
    public static TheoryData<string> ScheduledCases() => Names("scheduled");
    public static TheoryData<string> ScheduledInYearCases() => Names("scheduledInYear");

    private static void AssertCalculate(JsonElement c)
    {
        var start = Date(Str(c, "start")!);
        var end = Date(Str(c, "end")!);
        var schedule = ScheduleOf(c);

        if (c.TryGetProperty("error", out _))
        {
            Assert.Throws<ArgumentException>(() => LeaveHoursCalculator.Calculate(start, end, Str(c, "startTime"), Str(c, "endTime"), schedule));
            return;
        }

        Assert.Equal(c.GetProperty("hours").GetDecimal(),
            LeaveHoursCalculator.Calculate(start, end, Str(c, "startTime"), Str(c, "endTime"), schedule));
    }

    private static void AssertInYear(JsonElement c)
    {
        var start = Date(Str(c, "start")!);
        var end = Date(Str(c, "end")!);
        var schedule = ScheduleOf(c);
        var total = LeaveHoursCalculator.Calculate(start, end, Str(c, "startTime"), Str(c, "endTime"), schedule);

        Assert.Equal(c.GetProperty("hours").GetDecimal(),
            LeaveHoursCalculator.HoursInYear(start, end, total, c.GetProperty("year").GetInt32(), schedule));
    }

    [Theory]
    [MemberData(nameof(CalculateCases))]
    public void Calculate_FollowsTheSharedVectors(string name) =>
        AssertCalculate(Fixture().GetProperty("calculate").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name));

    [Theory]
    [MemberData(nameof(InYearCases))]
    public void HoursInYear_FollowsTheSharedVectors(string name) =>
        AssertInYear(Fixture().GetProperty("hoursInYear").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name));

    [Theory]
    [MemberData(nameof(ScheduledCases))]
    public void Calculate_FollowsTheCompanyScheduleVectors(string name) =>
        AssertCalculate(Fixture().GetProperty("scheduled").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name));

    [Theory]
    [MemberData(nameof(ScheduledInYearCases))]
    public void HoursInYear_FollowsTheCompanyScheduleVectors(string name) =>
        AssertInYear(Fixture().GetProperty("scheduledInYear").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name));

    [Fact]
    public void HoursInYear_ForALegacyRowWithoutStoredHours_FallsBackToTheWorkingDayRule()
    {
        // Rows created before hours were stored have Hours = null; they must still be chargeable.
        Assert.Equal(40m, LeaveHoursCalculator.HoursInYear(Date("2026-06-01"), Date("2026-06-05"), null, 2026, WorkSchedule.Default));
    }

    [Fact]
    public void WorkdayHours_DefaultIsEight_BecauseTheDefaultCompanyWorksAnEightHourDay()
    {
        Assert.Equal(8m, HRNexus.Domain.Constants.AppConstants.Leave.WorkdayHours);
        Assert.Equal(8m, WorkSchedule.Default.WorkdayHours);
    }
}
