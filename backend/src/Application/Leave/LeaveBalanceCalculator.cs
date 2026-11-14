using HRNexus.Domain.Leave;
using HRNexus.Domain.Policy;

namespace HRNexus.Application.Leave;

/// <summary>One type's line on a balance or summary: what was used and, where the type has a balance, what is left.</summary>
/// <param name="Key">The type key.</param>
/// <param name="Label">The name people see.</param>
/// <param name="Colour">The tag colour.</param>
/// <param name="Pool">annual | sick | own | none.</param>
/// <param name="Active">False for a deactivated type that still has leave this year.</param>
/// <param name="Used">Hours this type used in the year.</param>
/// <param name="Entitlement">The balance the type is charged to (hours); null for a type with no balance, or when the view does not carry entitlements.</param>
/// <param name="PoolUsed">Hours used from that balance by every type sharing it (annual and emergency share one); null when there is no balance.</param>
/// <param name="Remaining">Entitlement minus pool used, never below zero; null when there is no balance.</param>
public record LeaveTypeBalanceDto(
    string   Key,
    string   Label,
    string   Colour,
    string   Pool,
    bool     Active,
    decimal  Used,
    decimal? Entitlement,
    decimal? PoolUsed,
    decimal? Remaining);

/// <summary>A leave as the balance views read it: type, dates and stored hours.</summary>
public record LeaveRow(string LeaveType, DateTime StartDate, DateTime EndDate, decimal? Hours);

/// <summary>What the views show for one person and year; the old fixed fields are kept next to <see cref="Types"/>.</summary>
public record LeaveBalanceResult(
    IReadOnlyList<LeaveTypeBalanceDto> Types,
    decimal AnnualPoolUsed,
    decimal SickPoolUsed,
    decimal AnnualUsed,
    decimal SickUsed,
    decimal EmergencyUsed,
    decimal UnpaidUsed,
    decimal NoShowUsed,
    decimal TotalUsed);

/// <summary>
/// Turns a person's leave into the per-type lines and totals the balance and summary views show. The one place
/// that charges hours to a year, so the three views (one person, everyone, summary) can never disagree: every
/// leave is counted by its own type's schedule (working days, or calendar days for a calendar-day type).
/// </summary>
public static class LeaveBalanceCalculator
{
    /// <param name="catalog">The company's leave types.</param>
    /// <param name="company">The company working week (a type may count calendar days instead).</param>
    /// <param name="leaves">The person's pending and approved leave touching the year.</param>
    /// <param name="year">The year the hours are charged to.</param>
    /// <param name="annualEntitlement">The person's annual hours incl. carry-over, or null when the view has no entitlements.</param>
    /// <param name="sickEntitlement">The person's sick hours, or null when the view has no entitlements.</param>
    public static LeaveBalanceResult Compute(LeaveCatalog catalog, WorkSchedule company, IEnumerable<LeaveRow> leaves,
        int year, decimal? annualEntitlement, decimal? sickEntitlement)
    {
        var used = catalog.Types.ToDictionary(t => t.Key, _ => 0m, StringComparer.OrdinalIgnoreCase);
        foreach (var l in leaves)
        {
            var hours = LeaveHoursCalculator.HoursInYear(l.StartDate, l.EndDate, l.Hours, year, catalog.ScheduleFor(l.LeaveType, company));
            used[l.LeaveType] = used.GetValueOrDefault(l.LeaveType) + hours;
        }

        decimal PoolUsed(string pool) => catalog.Types.Where(t => t.Pool == pool).Sum(t => used[t.Key]);
        var annualPool = PoolUsed(LeavePools.Annual);
        var sickPool = PoolUsed(LeavePools.Sick);

        var lines = new List<LeaveTypeBalanceDto>();
        foreach (var t in catalog.Types)
        {
            // An inactive type with no leave this year is noise; one that still has leave stays visible.
            if (!t.Active && used[t.Key] == 0m) continue;

            decimal? entitlement = t.Pool switch
            {
                LeavePools.Annual => annualEntitlement,
                LeavePools.Sick   => sickEntitlement,
                LeavePools.Own    => (t.AllowanceDays ?? 0m) * company.WorkdayHours,
                _                 => null,
            };
            decimal? poolUsed = t.Pool switch
            {
                LeavePools.Annual => annualPool,
                LeavePools.Sick   => sickPool,
                LeavePools.Own    => used[t.Key],
                _                 => null,
            };
            decimal? remaining = entitlement is { } e && poolUsed is { } p ? Math.Max(0m, e - p) : null;
            lines.Add(new LeaveTypeBalanceDto(t.Key, t.Label, t.Colour, t.Pool, t.Active, used[t.Key],
                poolUsed is null ? null : entitlement, poolUsed, remaining));
        }

        // Types the catalogue no longer lists cannot exist (a type with leave is never removed), but stored
        // keys are counted in the total regardless so nothing is silently dropped.
        var total = used.Values.Sum();
        return new LeaveBalanceResult(lines, annualPool, sickPool,
            used.GetValueOrDefault(LeaveTypeDefaults.AnnualKey), used.GetValueOrDefault(LeaveTypeDefaults.SickKey),
            used.GetValueOrDefault(LeaveTypeDefaults.EmergencyKey), used.GetValueOrDefault(LeaveTypeDefaults.UnpaidKey),
            used.GetValueOrDefault(LeaveTypeDefaults.NoShowKey), total);
    }
}
