using System.Globalization;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.Leave;

/// <summary>
/// The one place that turns a leave request into hours. A working day is Monday to Friday and
/// <see cref="AppConstants.Leave.WorkdayHours"/> long. Public holidays are not modelled (Phase 8).
/// The frontend mirrors this rule (projects/shared-lib leave-hours.ts); both are tested against
/// tests/fixtures/leave-hours.json.
/// </summary>
public static class LeaveHoursCalculator
{
    private const decimal Workday = AppConstants.Leave.WorkdayHours;

    /// <summary>
    /// Single weekday: the clock difference of the two times (capped at one workday), or a full day
    /// when no times are given. Several days: working days in the range x one workday (times are ignored).
    /// </summary>
    /// <exception cref="ArgumentException">The range or times are invalid, or contain no working day.</exception>
    public static decimal Calculate(DateTime startDate, DateTime endDate, string? startTime, string? endTime)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (start > end)
            throw new ArgumentException("StartDate must be on or before EndDate.");
        if ((startTime is null) != (endTime is null))
            throw new ArgumentException("Provide both a start time and an end time, or neither.");
        if (WorkingDays(start, end) == 0)
            throw new ArgumentException("Leave must include at least one working day (Monday to Friday).");

        if (start != end) return WorkingDays(start, end) * Workday;
        if (startTime is null) return Workday;

        var minutes = (ParseTime(endTime!) - ParseTime(startTime)).TotalMinutes;
        if (minutes <= 0)
            throw new ArgumentException("The end time must be after the start time.");
        return Math.Min((decimal)minutes / 60m, Workday);
    }

    /// <summary>
    /// The part of a leave charged to <paramref name="year"/>. A single day belongs wholly to its own
    /// year; a longer leave is split by the working days that fall in the year. <paramref name="totalHours"/>
    /// (the stored value) is only used for a single day; null (rows from before hours were stored) falls
    /// back to the working-day rule.
    /// </summary>
    public static decimal HoursInYear(DateTime startDate, DateTime endDate, decimal? totalHours, int year)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (start == end)
            return start.Year == year ? totalHours ?? (WorkingDays(start, end) * Workday) : 0m;

        var from = start > new DateTime(year, 1, 1) ? start : new DateTime(year, 1, 1);
        var to = end < new DateTime(year, 12, 31) ? end : new DateTime(year, 12, 31);
        return from > to ? 0m : WorkingDays(from, to) * Workday;
    }

    /// <summary>Monday to Friday days in the inclusive range.</summary>
    public static int WorkingDays(DateTime from, DateTime to)
    {
        var days = 0;
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        return days;
    }

    private static TimeSpan ParseTime(string hhmm)
    {
        if (!TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out var t))
            throw new ArgumentException($"'{hhmm}' is not a valid time (use HH:mm).");
        return t;
    }
}
