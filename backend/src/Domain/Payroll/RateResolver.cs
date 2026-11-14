namespace HRNexus.Domain.Payroll;

/// <summary>The rate row that applies to a person for a month, or why none does.</summary>
public sealed record RateResolution(RateRule? Rule, string? WarningCode);

/// <summary>
/// Picks the rate row in force for a scheme, citizenship class and age: the latest row whose
/// <c>EffectiveFrom</c> is on or before the FIRST DAY of the month. A newer row that starts inside the month
/// is not guessed at (no day-by-day split): it raises <c>rate-change-in-month</c> instead.
/// </summary>
public static class RateResolver
{
    public const string Missing = "missing-rate";
    public const string AgeNotCovered = "age-not-covered";
    public const string ChangeInMonth = "rate-change-in-month";

    public static RateResolution Resolve(IEnumerable<RateRule> rules, string scheme, string citizenshipClass, int age,
        DateOnly monthStart, DateOnly monthEnd)
    {
        var candidates = rules.Where(r => r.Scheme == scheme && r.CitizenshipClass == citizenshipClass).ToList();
        if (candidates.Count == 0) return new RateResolution(null, Missing);

        var forAge = candidates.Where(r => r.AgeFrom <= age && (r.AgeTo is null || age <= r.AgeTo)).ToList();
        if (forAge.Count == 0) return new RateResolution(null, AgeNotCovered);

        var inForce = forAge.Where(r => r.EffectiveFrom <= monthStart)
            .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.AgeFrom)
            .FirstOrDefault();
        var changesInside = forAge.Any(r => r.EffectiveFrom > monthStart && r.EffectiveFrom <= monthEnd);

        if (changesInside) return new RateResolution(null, ChangeInMonth);
        return inForce is null ? new RateResolution(null, Missing) : new RateResolution(inForce, null);
    }
}
