using HRNexus.Domain.Constants;

namespace HRNexus.Application.Common;

public static class ShiftHours
{
    /// <summary>
    /// Floors an hours value DOWN to the nearest half-hour (0.5) block. Hourly jobs are billed in
    /// completed 30-minute blocks, so a partial block earns nothing: 1h29m → 1.0, 1h30m → 1.5,
    /// 1h45m → 1.5, sub-30-min → 0.
    /// </summary>
    public static decimal FloorToHalf(decimal hours) => Math.Floor(hours * 2m) / 2m;

    /// <summary>
    /// The hours a shift is billed and shown for. Hourly work counts only completed 30-min blocks
    /// (floored to 0.5); fixed packages (e.g. 包夜) keep their exact clock hours since their pay is a
    /// flat rate regardless of the exact minutes worked.
    /// </summary>
    public static decimal BillableHours(string payType, decimal exactHours) =>
        string.Equals(payType, AppConstants.PayType.Fixed, StringComparison.OrdinalIgnoreCase)
            ? exactHours
            : FloorToHalf(exactHours);

    /// <summary>
    /// Parses a StartTime/EndTime pair ("HH:mm") and returns shift duration in hours,
    /// handling midnight crossover (e.g. 22:00 → 02:00).
    /// </summary>
    public static decimal CalcHours(string startTime, string endTime)
    {
        var s = TimeSpan.Parse(startTime);
        var e = TimeSpan.Parse(endTime);
        // Identical times → full 24-hour shift (midnight-wrap formula handles this correctly)
        var diff = e > s ? e - s : e + TimeSpan.FromHours(24) - s;
        return (decimal)diff.TotalHours;
    }

    /// <summary>
    /// Duration in hours between an explicit start date+time and end date+time. Use this for stored
    /// shifts now that an end date is recorded — it supports spans beyond 24 hours (multi-day) rather
    /// than the single-overnight assumption of the time-only overload.
    /// </summary>
    public static decimal CalcHours(DateOnly startDate, string startTime, DateOnly endDate, string endTime)
    {
        var start = startDate.ToDateTime(TimeOnly.Parse(startTime));
        var end   = endDate.ToDateTime(TimeOnly.Parse(endTime));
        return (decimal)(end - start).TotalHours;
    }

    /// <summary>
    /// Infers the end date for paths that capture only an end time (staff clock-out, manual entry):
    /// the next day when the end time is at or before the start time (overnight), otherwise the same
    /// day. Preserves the historical single-overnight ≤24h behaviour for those flows.
    /// </summary>
    public static DateOnly DeriveEndDate(DateOnly workDate, string startTime, string endTime) =>
        TimeOnly.Parse(endTime) <= TimeOnly.Parse(startTime) ? workDate.AddDays(1) : workDate;

    /// <summary>Absolute minute offset of a date+time from a fixed epoch (DayNumber*1440 + HH*60 + mm).
    /// Used to test whether two shift intervals overlap, multi-day aware.</summary>
    public static long AbsMinutes(DateOnly date, string hhmm) =>
        (long)date.DayNumber * 1440 + int.Parse(hhmm[..2]) * 60 + int.Parse(hhmm[3..]);
}
