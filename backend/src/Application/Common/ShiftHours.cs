namespace HRNexus.Application.Common;

public static class ShiftHours
{
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
}
