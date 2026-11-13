namespace HRNexus.Domain.Policy;

/// <summary>
/// A company's working week: which weekdays are working days and how long a working day is.
/// Stored as a bit mask (Monday = 1 ... Sunday = 64) so it is one small column and cheap to test.
/// A value object: it carries no behaviour that needs a database, so leave maths stays pure and testable.
/// </summary>
public readonly record struct WorkSchedule(int DaysMask, decimal WorkdayHours)
{
    /// <summary>Monday to Friday, 8 hours: the default for a company that has not chosen otherwise.</summary>
    public static readonly WorkSchedule Default = new(MondayToFriday, 8m);

    private const int MondayToFriday = 1 | 2 | 4 | 8 | 16;

    /// <summary>Builds a schedule from ISO weekday numbers (1 = Monday ... 7 = Sunday).</summary>
    public static WorkSchedule FromIsoDays(IEnumerable<int> isoDays, decimal workdayHours)
    {
        var mask = 0;
        foreach (var d in isoDays)
        {
            if (d is < 1 or > 7) throw new ArgumentException($"'{d}' is not a weekday number (1 = Monday ... 7 = Sunday).");
            mask |= 1 << (d - 1);
        }
        return new WorkSchedule(mask, workdayHours);
    }

    /// <summary>The working days as ISO weekday numbers, in order.</summary>
    public int[] IsoDays
    {
        get
        {
            var mask = DaysMask;
            return Enumerable.Range(1, 7).Where(d => (mask & (1 << (d - 1))) != 0).ToArray();
        }
    }

    public bool IsWorkingDay(DayOfWeek day) => (DaysMask & (1 << (((int)day + 6) % 7))) != 0;

    /// <summary>Working days in the inclusive range.</summary>
    public int WorkingDaysBetween(DateTime from, DateTime to)
    {
        var days = 0;
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            if (IsWorkingDay(d.DayOfWeek)) days++;
        return days;
    }
}
