using HRNexus.Domain.Constants;
using HRNexus.Domain.Policy;

namespace HRNexus.Domain.Entities;

/// <summary>
/// The company's HR rules that differ from one company to the next: the working week, the length of
/// a working day, leave limits, the carry-over rule and the payroll unpaid-leave divisor. There is one
/// row per installation (one installation serves one company). When no row exists the defaults apply,
/// and the defaults equal the rules the product shipped with, so an untouched installation is unchanged.
/// </summary>
public class CompanyPolicy
{
    public const int SingletonId = 1;

    public int      Id                     { get; private set; } = SingletonId;
    public int      WorkingDaysMask        { get; private set; }
    public decimal  WorkdayHours           { get; private set; }
    public int      AnnualLeaveAdvanceDays { get; private set; }
    public int      MaxLeaveDays           { get; private set; }
    /// <summary>none = unused leave lapses; capped = up to <see cref="CarryOverMaxHours"/> carries forward; unlimited = all unused leave carries forward (cumulative).</summary>
    public string   CarryOverMode          { get; private set; } = AppConstants.Policy.CarryOverCapped;
    public decimal  CarryOverMaxHours      { get; private set; }
    /// <summary>Daily pay rate divisor for unpaid-leave deductions: monthly basic / divisor x unpaid days (Phase 8).</summary>
    public decimal  UnpaidLeaveDivisor     { get; private set; }
    /// <summary>Counts saves; a save made from a stale copy is refused.</summary>
    public int      Version                { get; private set; }
    public DateTime UpdatedAt              { get; private set; }
    public string?  UpdatedBy              { get; private set; }

    private CompanyPolicy() { }

    public WorkSchedule Schedule => new(WorkingDaysMask, WorkdayHours);

    /// <summary>The policy in force when none is saved. Not persisted until an admin saves.</summary>
    public static CompanyPolicy Default() => new()
    {
        WorkingDaysMask        = WorkSchedule.Default.DaysMask,
        WorkdayHours           = WorkSchedule.Default.WorkdayHours,
        AnnualLeaveAdvanceDays = AppConstants.Leave.AnnualLeaveAdvanceDays,
        MaxLeaveDays           = AppConstants.Leave.MaxLeaveDays,
        CarryOverMode          = AppConstants.Policy.CarryOverCapped,
        CarryOverMaxHours      = AppConstants.Policy.DefaultCarryOverMaxHours,
        UnpaidLeaveDivisor     = AppConstants.Policy.DefaultUnpaidLeaveDivisor,
        Version                = 0,
        UpdatedAt              = DateTime.UtcNow,
    };

    /// <exception cref="ArgumentException">Any value is outside its allowed range.</exception>
    public void Update(IReadOnlyCollection<int> isoDays, decimal workdayHours, int advanceDays, int maxLeaveDays,
        string carryOverMode, decimal carryOverMaxHours, decimal unpaidLeaveDivisor, string? updatedBy)
    {
        if (isoDays.Count == 0)
            throw new ArgumentException("Choose at least one working day.");
        if (isoDays.Any(d => d is < 1 or > 7) || isoDays.Distinct().Count() != isoDays.Count)
            throw new ArgumentException("Working days must be distinct weekdays (1 = Monday ... 7 = Sunday).");
        if (workdayHours is <= 0 or > 24)
            throw new ArgumentException("Hours per working day must be more than 0 and at most 24.");
        if (advanceDays is < 0 or > 60)
            throw new ArgumentException("Annual leave notice must be between 0 and 60 days.");
        if (maxLeaveDays is < 1 or > 366)
            throw new ArgumentException("The longest single leave request must be between 1 and 366 days.");
        var mode = carryOverMode?.Trim().ToLowerInvariant() ?? "";
        if (mode is not (AppConstants.Policy.CarryOverNone or AppConstants.Policy.CarryOverCapped or AppConstants.Policy.CarryOverUnlimited))
            throw new ArgumentException("Carry-over must be 'none', 'capped' or 'unlimited'.");
        if (carryOverMaxHours is < 0 or > 720)
            throw new ArgumentException("The carry-over cap must be between 0 and 720 hours.");
        if (unpaidLeaveDivisor is < 20 or > 31)
            throw new ArgumentException("The unpaid-leave divisor must be between 20 and 31.");

        WorkingDaysMask        = WorkSchedule.FromIsoDays(isoDays, workdayHours).DaysMask;
        WorkdayHours           = workdayHours;
        AnnualLeaveAdvanceDays = advanceDays;
        MaxLeaveDays           = maxLeaveDays;
        CarryOverMode          = mode;
        CarryOverMaxHours      = carryOverMaxHours;
        UnpaidLeaveDivisor     = unpaidLeaveDivisor;
        Version++;
        UpdatedAt              = DateTime.UtcNow;
        UpdatedBy              = updatedBy;
    }
}
