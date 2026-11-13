using System.Globalization;
using HRNexus.Domain.Policy;

namespace HRNexus.Application.Leave;

/// <summary>
/// The one place that turns a leave request into hours, using the company's <see cref="WorkSchedule"/>
/// (which weekdays are working days and how long a working day is; Monday to Friday, 8 hours by default).
/// Public holidays are not modelled (Phase 8). It stays pure: callers pass the schedule from the company
/// policy. The frontend mirrors this rule (projects/shared-lib leave-hours.ts); both are tested against
/// tests/fixtures/leave-hours.json.
/// </summary>
public static class LeaveHoursCalculator
{
    /// <summary>
    /// Single working day: the clock difference of the two times (capped at one working day), or a full
    /// day when no times are given. Several days: working days in the range x one working day (times are
    /// ignored).
    /// </summary>
    /// <exception cref="ArgumentException">The range or times are invalid, or contain no working day.</exception>
    public static decimal Calculate(DateTime startDate, DateTime endDate, string? startTime, string? endTime, WorkSchedule schedule)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (start > end)
            throw new ArgumentException("StartDate must be on or before EndDate.");
        if ((startTime is null) != (endTime is null))
            throw new ArgumentException("Provide both a start time and an end time, or neither.");
        var workingDays = schedule.WorkingDaysBetween(start, end);
        if (workingDays == 0)
            throw new ArgumentException("Leave must include at least one working day.");

        if (start != end) return workingDays * schedule.WorkdayHours;
        if (startTime is null) return schedule.WorkdayHours;

        var minutes = (ParseTime(endTime!) - ParseTime(startTime)).TotalMinutes;
        if (minutes <= 0)
            throw new ArgumentException("The end time must be after the start time.");
        return Math.Min((decimal)minutes / 60m, schedule.WorkdayHours);
    }

    /// <summary>
    /// The part of a leave charged to <paramref name="year"/>. A single day belongs wholly to its own
    /// year; a longer leave is split by the working days that fall in the year. <paramref name="totalHours"/>
    /// (the stored value) is only used for a single day; null (rows from before hours were stored) falls
    /// back to the working-day rule.
    /// </summary>
    public static decimal HoursInYear(DateTime startDate, DateTime endDate, decimal? totalHours, int year, WorkSchedule schedule)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (start == end)
            return start.Year == year ? totalHours ?? (schedule.WorkingDaysBetween(start, end) * schedule.WorkdayHours) : 0m;

        var from = start > new DateTime(year, 1, 1) ? start : new DateTime(year, 1, 1);
        var to = end < new DateTime(year, 12, 31) ? end : new DateTime(year, 12, 31);
        return from > to ? 0m : schedule.WorkingDaysBetween(from, to) * schedule.WorkdayHours;
    }

    private static TimeSpan ParseTime(string hhmm)
    {
        if (!TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out var t))
            throw new ArgumentException($"'{hhmm}' is not a valid time (use HH:mm).");
        return t;
    }
}
