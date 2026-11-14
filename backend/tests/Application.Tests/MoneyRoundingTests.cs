using System.Globalization;
using HRNexus.Domain.Payroll;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// WHY these tests matter: every statutory amount is rounded by the rule its table declares, and a payslip
/// that is a sen out is a real underpayment or overpayment. The helper must be exact (decimal, never float),
/// must round a midpoint AWAY from zero (the way money is rounded on paper), and must not depend on the
/// machine's culture.
/// </summary>
public class MoneyRoundingTests
{
    [Theory]
    [InlineData("123.456", "123.456")]
    [InlineData("0", "0")]
    public void None_leaves_the_value_exactly_as_it_is(string input, string expected) =>
        Assert.Equal(D(expected), MoneyRounding.Round(D(input), RoundingRule.None));

    [Theory]
    [InlineData("0.124", "0.12")]
    [InlineData("0.125", "0.13")]   // a midpoint goes away from zero, not to the even digit
    [InlineData("0.135", "0.14")]
    [InlineData("1234.5", "1234.50")]
    [InlineData("-0.125", "-0.13")]
    public void Nearest_cent_rounds_a_midpoint_away_from_zero(string input, string expected) =>
        Assert.Equal(D(expected), MoneyRounding.Round(D(input), RoundingRule.NearestCent));

    [Theory]
    [InlineData("1.02", "1.00")]
    [InlineData("1.025", "1.05")]   // exactly half of 5 sen goes up
    [InlineData("1.03", "1.05")]
    [InlineData("1.07", "1.05")]
    [InlineData("1.08", "1.10")]
    [InlineData("0", "0.00")]
    public void Nearest_5_sen_rounds_to_a_multiple_of_five_sen(string input, string expected) =>
        Assert.Equal(D(expected), MoneyRounding.Round(D(input), RoundingRule.Nearest5Sen));

    [Theory]
    [InlineData("123.00", "123")]    // already a whole ringgit: unchanged, not bumped
    [InlineData("123.01", "124")]
    [InlineData("123.99", "124")]
    [InlineData("0.001", "1")]
    [InlineData("0", "0")]
    public void Up_to_next_ringgit_never_rounds_down(string input, string expected) =>
        Assert.Equal(D(expected), MoneyRounding.Round(D(input), RoundingRule.UpToNextRinggit));

    [Fact]
    public void Decimal_arithmetic_is_exact_where_a_float_would_drift()
    {
        // 0.1 + 0.2 is 0.30000000000000004 as a double; as a decimal it is exactly 0.3.
        Assert.Equal(D("0.30"), MoneyRounding.Round(D("0.1") + D("0.2"), RoundingRule.NearestCent));
    }

    [Fact]
    public void The_result_does_not_depend_on_the_current_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // decimal comma
            Assert.Equal(D("1.05"), MoneyRounding.Round(D("1.025"), RoundingRule.Nearest5Sen));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void An_unknown_rule_is_refused_rather_than_ignored() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneyRounding.Round(1m, (RoundingRule)99));

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
}
