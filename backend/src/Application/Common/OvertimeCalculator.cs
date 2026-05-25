using HRNexus.Domain.Entities;

namespace HRNexus.Application.Common;

public static class OvertimeCalculator
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

    /// <summary>
    /// Selects the applicable OT rules for an employee given all active rules.
    /// Role-specific rules take priority; falls back to wildcard (*) rules.
    /// </summary>
    public static List<OvertimeRule> ResolveRules(
        IList<OvertimeRule> allRules,
        IEnumerable<string> empRoles)
    {
        var roles = empRoles.ToList();
        var roleRules = allRules
            .Where(r => r.Role.Split(',', StringSplitOptions.TrimEntries)
                .Any(rp => roles.Any(er => er.Equals(rp, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        return roleRules.Count > 0
            ? roleRules
            : allRules.Where(r => r.Role == "*").ToList();
    }

    /// <summary>
    /// Returns the OT threshold hours from the first (lowest) applicable rule,
    /// defaulting to 8h when no rules are configured.
    /// </summary>
    public static decimal GetThreshold(IList<OvertimeRule> applicableRules) =>
        applicableRules.Count > 0
            ? applicableRules.OrderBy(r => r.ThresholdHours).First().ThresholdHours
            : 8m;

    /// <summary>
    /// Given a shift duration in hours, a base hourly rate, and a set of OT rules
    /// (pre-filtered for this employee's role), returns the gross pay.
    /// Rules are applied in order of ThresholdHours ascending.
    /// </summary>
    public static decimal Calculate(
        decimal hoursWorked,
        decimal hourlyRate,
        IEnumerable<OvertimeRule> rules)
    {
        var activeRules = rules
            .Where(r => r.IsActive)
            .OrderBy(r => r.ThresholdHours)
            .ToList();

        if (!activeRules.Any())
            return hoursWorked * hourlyRate;

        decimal pay = 0m;
        decimal remaining = hoursWorked;
        decimal prevThreshold = 0m;

        foreach (var rule in activeRules)
        {
            if (remaining <= 0) break;
            var regularHours = Math.Min(remaining, rule.ThresholdHours - prevThreshold);
            if (regularHours > 0)
            {
                pay += regularHours * hourlyRate;
                remaining -= regularHours;
            }
            prevThreshold = rule.ThresholdHours;
        }

        // Any remaining hours beyond the last threshold get the last rule's multiplier
        if (remaining > 0 && activeRules.Any())
        {
            var lastRule = activeRules.Last();
            pay += remaining * hourlyRate * lastRule.Multiplier;
        }
        else if (remaining > 0)
        {
            pay += remaining * hourlyRate;
        }

        return Math.Round(pay, 2);
    }
}
