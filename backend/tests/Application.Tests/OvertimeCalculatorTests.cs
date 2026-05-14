using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

public class OvertimeCalculatorTests
{
    // ── CalcHours ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("09:00", "17:00", 8.0)]
    [InlineData("09:00", "09:00", 24.0)]   // full 24-hour shift
    [InlineData("22:00", "02:00", 4.0)]    // midnight crossing
    [InlineData("23:00", "06:00", 7.0)]    // nightclub overnight
    [InlineData("18:00", "00:00", 6.0)]    // ends exactly at midnight
    [InlineData("00:00", "04:30", 4.5)]    // starts at midnight
    public void CalcHours_ReturnsCorrectDuration(string start, string end, double expected)
    {
        var result = OvertimeCalculator.CalcHours(start, end);
        Assert.Equal((decimal)expected, result);
    }

    // ── GetThreshold ─────────────────────────────────────────────────────────

    [Fact]
    public void GetThreshold_NoRules_ReturnsDefault8()
    {
        var result = OvertimeCalculator.GetThreshold([]);
        Assert.Equal(8m, result);
    }

    [Fact]
    public void GetThreshold_SingleRule_ReturnsThatThreshold()
    {
        var rules = new List<OvertimeRule> { MakeRule("*", 6m, 1.5m) };
        Assert.Equal(6m, OvertimeCalculator.GetThreshold(rules));
    }

    [Fact]
    public void GetThreshold_MultipleRules_ReturnsLowest()
    {
        var rules = new List<OvertimeRule>
        {
            MakeRule("*", 10m, 2.0m),
            MakeRule("*",  6m, 1.5m),
            MakeRule("*",  8m, 1.75m),
        };
        Assert.Equal(6m, OvertimeCalculator.GetThreshold(rules));
    }

    // ── ResolveRules ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolveRules_NoRules_ReturnsEmpty()
    {
        var result = OvertimeCalculator.ResolveRules([], ["bartender"]);
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveRules_WildcardOnly_ReturnsWildcard()
    {
        var rules  = new List<OvertimeRule> { MakeRule("*", 8m, 1.5m) };
        var result = OvertimeCalculator.ResolveRules(rules, ["bartender"]);
        Assert.Single(result);
        Assert.Equal("*", result[0].Role);
    }

    [Fact]
    public void ResolveRules_RoleMatchExists_ReturnsRoleRule_NotWildcard()
    {
        var rules = new List<OvertimeRule>
        {
            MakeRule("*",          8m, 1.5m),
            MakeRule("bartender",  6m, 1.5m),
        };
        var result = OvertimeCalculator.ResolveRules(rules, ["bartender"]);
        Assert.Single(result);
        Assert.Equal("bartender", result[0].Role);
    }

    [Fact]
    public void ResolveRules_CaseInsensitiveMatch()
    {
        var rules  = new List<OvertimeRule> { MakeRule("Bartender", 6m, 1.5m) };
        var result = OvertimeCalculator.ResolveRules(rules, ["bartender"]);
        Assert.Single(result);
    }

    [Fact]
    public void ResolveRules_NoRoleMatch_FallsBackToWildcard()
    {
        var rules = new List<OvertimeRule>
        {
            MakeRule("*",       8m, 1.5m),
            MakeRule("manager", 6m, 1.5m),
        };
        var result = OvertimeCalculator.ResolveRules(rules, ["bartender"]);
        Assert.Single(result);
        Assert.Equal("*", result[0].Role);
    }

    // ── Calculate ────────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_NoRules_StraightTimePay()
    {
        var pay = OvertimeCalculator.Calculate(8m, 10m, []);
        Assert.Equal(80m, pay);
    }

    [Fact]
    public void Calculate_BelowThreshold_StraightTimePay()
    {
        var rules = new List<OvertimeRule> { MakeRule("*", 8m, 1.5m) };
        var pay   = OvertimeCalculator.Calculate(6m, 10m, rules);
        Assert.Equal(60m, pay);   // 6h × RM10 = RM60
    }

    [Fact]
    public void Calculate_ExactlyAtThreshold_StraightTimePay()
    {
        var rules = new List<OvertimeRule> { MakeRule("*", 8m, 1.5m) };
        var pay   = OvertimeCalculator.Calculate(8m, 10m, rules);
        Assert.Equal(80m, pay);   // 8h × RM10 = RM80
    }

    [Fact]
    public void Calculate_OvertimeHours_AppliesMultiplier()
    {
        var rules = new List<OvertimeRule> { MakeRule("*", 8m, 1.5m) };
        var pay   = OvertimeCalculator.Calculate(10m, 10m, rules);
        // 8h × RM10 + 2h × RM10 × 1.5 = RM80 + RM30 = RM110
        Assert.Equal(110m, pay);
    }

    [Fact]
    public void Calculate_NightclubShift_MidnightCrossover()
    {
        // 22:00 → 04:00 = 6 hours, below 8h threshold
        var hours = OvertimeCalculator.CalcHours("22:00", "04:00");
        var rules = new List<OvertimeRule> { MakeRule("*", 8m, 1.5m) };
        var pay   = OvertimeCalculator.Calculate(hours, 15m, rules);
        Assert.Equal(6m, hours);
        Assert.Equal(90m, pay);   // 6h × RM15 = RM90 (no OT)
    }

    [Fact]
    public void Calculate_LongShift_ExceedsThresholdWithOt()
    {
        // 9 hours, threshold 8h at 1.5×, rate RM12
        var rules = new List<OvertimeRule> { MakeRule("*", 8m, 1.5m) };
        var pay   = OvertimeCalculator.Calculate(9m, 12m, rules);
        // 8h × RM12 + 1h × RM12 × 1.5 = RM96 + RM18 = RM114
        Assert.Equal(114m, pay);
    }

    [Fact]
    public void Calculate_InactiveRule_Ignored()
    {
        var rule = MakeRule("*", 8m, 1.5m);
        rule.Update(rule.Role, rule.ThresholdHours, rule.Multiplier, isActive: false);
        var pay = OvertimeCalculator.Calculate(10m, 10m, [rule]);
        // Inactive rule ignored → straight time
        Assert.Equal(100m, pay);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static OvertimeRule MakeRule(string role, decimal thresholdHours, decimal multiplier)
        => OvertimeRule.Create(role, thresholdHours, multiplier);
}
