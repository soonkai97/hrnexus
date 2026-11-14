
namespace HRNexus.Domain.Payroll;

/// <summary>
/// The only place money is rounded. Exact <see cref="decimal"/> arithmetic (never floating point), a midpoint
/// goes away from zero, and nothing here reads the current culture. A scheme's rounding rule is data in its
/// rate table; callers pass it in.
/// </summary>
public static class MoneyRounding
{
    public static decimal Round(decimal value, RoundingRule rule) => rule switch
    {
        RoundingRule.None            => value,
        RoundingRule.NearestCent     => Math.Round(value, 2, MidpointRounding.AwayFromZero),
        RoundingRule.Nearest5Sen     => Math.Round(value * 20m, 0, MidpointRounding.AwayFromZero) / 20m,
        RoundingRule.UpToNextRinggit => Math.Ceiling(value),
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown rounding rule."),
    };
}
