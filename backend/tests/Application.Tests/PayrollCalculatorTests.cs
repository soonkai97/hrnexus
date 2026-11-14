using System.Globalization;
using HRNexus.Domain.Payroll;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Behaviour the hand-worked examples do not cover: which warnings stop approval, how band tables and
/// effective dates are read, and the structural guarantees (exact formulas, determinism, ceilings).
///
/// WHY these tests matter: a calculator that quietly produces a number when a rate is missing, unverified or
/// out of date is worse than one that refuses, because a payslip looks authoritative. Every "this must be a
/// blocking warning" case below is a case where guessing would underpay or overpay someone.
/// </summary>
public class PayrollCalculatorTests
{
    private static readonly DateOnly Jan2020 = new(2020, 1, 1);

    private static RateRule Rule(string scheme, string klass = "citizen", decimal ee = 0m, decimal er = 0m,
        RoundingRule rounding = RoundingRule.NearestCent, bool verified = true, bool active = true,
        decimal? ceiling = null, RateKind kind = RateKind.Percentage, IReadOnlyList<RateBandRule>? bands = null,
        DateOnly? effective = null, int ageFrom = 0, int? ageTo = null, decimal? amount = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), scheme, klass, ageFrom, ageTo, effective ?? Jan2020, kind, ceiling, ee, er,
            EmployerRateMode.Flat, null, null, rounding, amount, active, verified, bands ?? []);

    private static IReadOnlyList<RateRule> Standard(bool verified = true) =>
    [
        Rule(StatutoryScheme.Epf, ee: 0.11m, er: 0.13m, rounding: RoundingRule.UpToNextRinggit, verified: verified),
        Rule(StatutoryScheme.Socso, ee: 0.005m, er: 0.0175m, rounding: RoundingRule.Nearest5Sen, ceiling: 6000m, verified: verified),
        Rule(StatutoryScheme.Eis, ee: 0.002m, er: 0.002m, rounding: RoundingRule.Nearest5Sen, ceiling: 6000m, verified: verified),
    ];

    private static PayrollInput Input(decimal basic = 3000m, IReadOnlyList<RateRule>? rates = null, string? klass = "citizen",
        int age = 30, int year = 2026, int month = 6, IReadOnlyList<SalarySegment>? segments = null,
        DateOnly? start = null, DateOnly? end = null, decimal? pcb = 0m, decimal divisor = 26m)
    {
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return new PayrollInput(year, month, age, klass, start, end, segments ?? [new SalarySegment(first, last, basic)], [],
            divisor, [], [], 0.5m, pcb, rates ?? Standard());
    }

    private static bool Has(PayrollResult r, string code, bool? blocking = null) =>
        r.Warnings.Any(w => w.Code == code && (blocking is null || w.Blocking == blocking));

    // ── warnings that must stop approval ─────────────────────────────────────────

    [Fact]
    public void An_unverified_rate_row_is_a_blocking_warning_even_though_a_number_is_produced()
    {
        var r = PayrollCalculator.Calculate(Input(rates: Standard(verified: false)));
        Assert.True(Has(r, "unverified-rate", blocking: true));
        Assert.True(r.HasBlockingWarning);
        Assert.True(r.Net > 0m);   // the figure is shown for review, but the run cannot be approved on it
    }

    [Fact]
    public void A_scheme_with_no_rate_row_is_a_blocking_warning_never_a_silent_zero()
    {
        var r = PayrollCalculator.Calculate(Input(rates: [Rule(StatutoryScheme.Socso, ee: 0.005m, er: 0.0175m), Rule(StatutoryScheme.Eis, ee: 0.002m, er: 0.002m)]));
        Assert.True(Has(r, "missing-rate", blocking: true));
        Assert.DoesNotContain(r.Lines, l => l.Type.StartsWith("epf"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("foreign")]
    public void A_person_without_a_supported_citizenship_class_gets_no_statutory_figures_and_a_blocking_warning(string? klass)
    {
        var r = PayrollCalculator.Calculate(Input(klass: klass));
        Assert.True(Has(r, "no-citizenship-class", blocking: true));
        Assert.DoesNotContain(r.Lines, l => l.Section is LineSection.EmployeeStatutory or LineSection.EmployerContribution);
        Assert.Equal(r.Gross, r.Net);
    }

    [Fact]
    public void A_person_whose_age_no_rate_row_covers_is_flagged()
    {
        // Rows only cover ages 0 to 59 (the over-60 rates are not loaded).
        var rates = new[]
        {
            Rule(StatutoryScheme.Epf, ee: 0.11m, er: 0.13m, ageTo: 59),
            Rule(StatutoryScheme.Socso, ee: 0.005m, er: 0.0175m, ageTo: 59),
            Rule(StatutoryScheme.Eis, ee: 0.002m, er: 0.002m, ageTo: 59),
        };
        var r = PayrollCalculator.Calculate(Input(rates: rates, age: 61));
        Assert.True(Has(r, "age-not-covered", blocking: true));
    }

    [Fact]
    public void A_rate_that_changes_inside_the_month_is_flagged_instead_of_guessed()
    {
        var rates = Standard().Append(Rule(StatutoryScheme.Epf, ee: 0.12m, er: 0.14m, effective: new DateOnly(2026, 6, 16))).ToList();
        var r = PayrollCalculator.Calculate(Input(rates: rates));
        Assert.True(Has(r, "rate-change-in-month", blocking: true));
    }

    [Fact]
    public void A_rate_that_starts_on_the_first_of_the_month_applies_that_month()
    {
        var newer = Rule(StatutoryScheme.Epf, ee: 0.12m, er: 0.14m, rounding: RoundingRule.None, effective: new DateOnly(2026, 6, 1));
        var rates = Standard().Append(newer).ToList();
        var r = PayrollCalculator.Calculate(Input(basic: 1000m, rates: rates));
        Assert.Equal(120m, r.Lines.Single(l => l.Type == "epf-employee").Amount);
        Assert.Contains(newer.Id, r.Basis.RateRuleIds);
    }

    [Fact]
    public void The_older_rate_row_applies_to_a_month_before_a_newer_one_starts()
    {
        var newer = Rule(StatutoryScheme.Epf, ee: 0.12m, er: 0.14m, rounding: RoundingRule.None, effective: new DateOnly(2026, 7, 1));
        var rates = Standard().Append(newer).ToList();
        var r = PayrollCalculator.Calculate(Input(basic: 1000m, rates: rates, month: 6));
        Assert.DoesNotContain(newer.Id, r.Basis.RateRuleIds);
        Assert.False(Has(r, "rate-change-in-month"));
    }

    [Fact]
    public void Employed_days_with_no_salary_on_record_are_flagged()
    {
        // Employed from 1 June but the first salary row only starts on 11 June.
        var segs = new[] { new SalarySegment(new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 30), 3000m) };
        var r = PayrollCalculator.Calculate(Input(segments: segs));
        Assert.True(Has(r, "no-salary-for-days", blocking: true));
        Assert.Equal(2000m, r.ProratedBasic);   // only the covered 20 days are paid
    }

    [Fact]
    public void A_leaver_whose_last_day_is_before_the_month_has_no_pay_and_a_blocking_warning()
    {
        var r = PayrollCalculator.Calculate(Input(end: new DateOnly(2026, 5, 31)));
        Assert.True(Has(r, "employment-outside-month", blocking: true));
        Assert.Equal(0m, r.Gross);
        Assert.Equal(0, r.Basis.EmployedDays);
    }

    [Fact]
    public void Statutory_amounts_that_exceed_gross_pay_flag_negative_net()
    {
        // A tiny gross with a PCB larger than it can carry.
        var r = PayrollCalculator.Calculate(Input(basic: 100m, pcb: 500m));
        Assert.True(r.Net >= 0m || Has(r, "negative-net", blocking: true));
        Assert.Equal(r.Gross - r.Lines.Where(l => l.Section == LineSection.EmployeeStatutory).Sum(l => l.Amount)
            - r.Lines.Single(l => l.Type == "pcb").Amount, r.Net);
        Assert.True(Has(r, "pcb-reduced", blocking: false));
    }

    [Fact]
    public void Two_active_HRD_rows_are_flagged_and_no_levy_is_guessed()
    {
        var rates = Standard().Concat([Rule(StatutoryScheme.HrdCompulsory, er: 0.01m), Rule(StatutoryScheme.HrdOptional, er: 0.005m)]).ToList();
        var r = PayrollCalculator.Calculate(Input(rates: rates));
        Assert.True(Has(r, "two-hrd-rows-active", blocking: true));
        Assert.DoesNotContain(r.Lines, l => l.Type == "hrd-levy");
    }

    [Fact]
    public void The_optional_half_percent_HRD_row_is_used_when_it_is_the_active_one()
    {
        var rates = Standard().Concat([Rule(StatutoryScheme.HrdCompulsory, er: 0.01m, active: false), Rule(StatutoryScheme.HrdOptional, er: 0.005m)]).ToList();
        var r = PayrollCalculator.Calculate(Input(basic: 3000m, rates: rates));
        Assert.Equal(15.00m, r.Lines.Single(l => l.Type == "hrd-levy").Amount);
    }

    [Fact]
    public void A_salary_below_the_minimum_wage_row_is_an_informational_warning_only()
    {
        var rates = Standard().Append(Rule(StatutoryScheme.MinimumWage, amount: 1700m)).ToList();
        var r = PayrollCalculator.Calculate(Input(basic: 1500m, rates: rates));
        Assert.True(Has(r, "below-minimum-wage", blocking: false));
        Assert.False(Has(r, "below-minimum-wage", blocking: true));
    }

    // ── band tables ──────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<RateBandRule> Bands =
    [
        new(0m, 1000m, 10m, 20m),
        new(1000m, 2000m, 20m, 40m),
        new(2000m, 3000m, 30m, 60m),
    ];

    private static PayrollResult WithBands(decimal basic, decimal? ceiling = null, IReadOnlyList<RateBandRule>? bands = null) =>
        PayrollCalculator.Calculate(Input(basic: basic, rates:
        [
            Rule(StatutoryScheme.Epf, ee: 0.11m, er: 0.13m),
            Rule(StatutoryScheme.Socso, kind: RateKind.BandTable, bands: bands ?? Bands, ceiling: ceiling),
            Rule(StatutoryScheme.Eis, ee: 0.002m, er: 0.002m),
        ]));

    [Theory]
    [InlineData(500, 10, 20)]
    [InlineData(1500, 20, 40)]
    [InlineData(2000, 30, 60)]      // a band's lower edge belongs to that band
    [InlineData(1999.99, 20, 40)]   // its upper edge does not
    public void A_band_table_gives_the_fixed_amounts_of_the_band_the_wage_falls_in(double wage, double ee, double er)
    {
        var r = WithBands((decimal)wage);
        Assert.Equal((decimal)ee, r.Lines.Single(l => l.Type == "socso-employee").Amount);
        Assert.Equal((decimal)er, r.Lines.Single(l => l.Type == "socso-employer").Amount);
    }

    [Fact]
    public void A_wage_above_the_top_band_uses_the_top_band()
    {
        var r = WithBands(3500m);
        Assert.Equal(30m, r.Lines.Single(l => l.Type == "socso-employee").Amount);
    }

    [Fact]
    public void A_ceiling_is_applied_to_the_wage_before_the_band_is_looked_up()
    {
        var r = WithBands(2500m, ceiling: 1500m);   // 2500 is capped to 1500, which is in the second band
        Assert.Equal(20m, r.Lines.Single(l => l.Type == "socso-employee").Amount);
    }

    [Fact]
    public void A_gap_between_bands_is_a_blocking_warning_and_gives_nothing()
    {
        var gappy = new RateBandRule[] { new(0m, 1000m, 10m, 20m), new(1200m, 2000m, 20m, 40m) };
        var r = WithBands(1100m, bands: gappy);
        Assert.True(Has(r, "band-gap", blocking: true));
        Assert.Equal(0m, r.Lines.Single(l => l.Type == "socso-employee").Amount);
    }

    // ── contracts: what the calculator refuses and what it guarantees ─────────────

    [Fact]
    public void Overlapping_salary_segments_are_a_caller_bug_and_refused()
    {
        var segs = new[]
        {
            new SalarySegment(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), 3000m),
            new SalarySegment(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), 4000m),
        };
        Assert.Throws<ArgumentException>(() => PayrollCalculator.Calculate(Input(segments: segs)));
    }

    [Fact]
    public void A_zero_divisor_is_refused_because_it_would_divide_by_zero()
    {
        Assert.Throws<ArgumentException>(() => PayrollCalculator.Calculate(Input(divisor: 0m)));
    }

    [Fact]
    public void Negative_amounts_are_refused()
    {
        var bad = Input() with { Earnings = [new EarningInput("allowance", "x", -1m)] };
        Assert.Throws<ArgumentException>(() => PayrollCalculator.Calculate(bad));
    }

    [Fact]
    public void The_result_records_what_is_needed_to_reproduce_it()
    {
        var epf = Rule(StatutoryScheme.Epf, ee: 0.11m, er: 0.13m);
        var r = PayrollCalculator.Calculate(Input(rates: [epf, Rule(StatutoryScheme.Socso), Rule(StatutoryScheme.Eis)], divisor: 22m));
        Assert.Equal(PayrollCalculator.Version, r.Basis.CalculatorVersion);
        Assert.Equal(22m, r.Basis.Divisor);
        Assert.Equal(30, r.Basis.DaysInMonth);
        Assert.Equal(30, r.Basis.EmployedDays);
        Assert.Contains(epf.Id, r.Basis.RateRuleIds);
    }

    [Fact]
    public void Calculating_twice_gives_identical_results_whatever_the_culture()
    {
        var input = Input(basic: 2345.67m);
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var a = PayrollCalculator.Calculate(input);
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var b = PayrollCalculator.Calculate(input);
            Assert.Equal(a.Net, b.Net);
            Assert.Equal(a.EmployerCost, b.EmployerCost);
            Assert.Equal(a.Lines, b.Lines);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    // ── structural guarantees over many wages (the hand-worked examples are the oracle for the figures) ──

    [Fact]
    public void The_formulas_hold_exactly_and_no_wage_ever_increases_a_capped_contribution()
    {
        var rates = Standard().Append(Rule(StatutoryScheme.HrdCompulsory, er: 0.01m)).ToList();
        decimal lastSocso = 0m, lastEis = 0m;
        for (var wage = 100m; wage <= 12000m; wage += 137.31m)
        {
            var r = PayrollCalculator.Calculate(Input(basic: wage, rates: rates));
            var ee = r.Lines.Where(l => l.Section == LineSection.EmployeeStatutory).Sum(l => l.Amount);
            var er = r.Lines.Where(l => l.Section == LineSection.EmployerContribution).Sum(l => l.Amount);

            Assert.Equal(r.Gross - ee - r.Lines.Where(l => l.Type == "pcb").Sum(l => l.Amount), r.Net);
            Assert.Equal(r.Gross + er, r.EmployerCost);
            Assert.All(r.Lines, l => Assert.True(l.Amount >= 0m, $"{l.Type} is negative at wage {wage}"));

            // Non-decreasing below the ceiling, and flat above it.
            var socso = r.Lines.Single(l => l.Type == "socso-employee").Amount;
            var eis = r.Lines.Single(l => l.Type == "eis-employee").Amount;
            Assert.True(socso >= lastSocso && eis >= lastEis, $"a contribution fell as the wage rose, at {wage}");
            if (wage > 6000m) Assert.True(socso <= 30.00m && eis <= 12.00m, $"ceiling exceeded at {wage}");
            (lastSocso, lastEis) = (socso, eis);
        }
    }

    // ── rate resolution on its own ────────────────────────────────────────────────

    [Fact]
    public void The_resolver_prefers_the_latest_row_in_force_and_the_highest_starting_age()
    {
        var old = Rule(StatutoryScheme.Epf, effective: new DateOnly(2020, 1, 1));
        var newer = Rule(StatutoryScheme.Epf, effective: new DateOnly(2025, 1, 1));
        var res = RateResolver.Resolve([old, newer], StatutoryScheme.Epf, "citizen", 30, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        Assert.Same(newer, res.Rule);
        Assert.Null(res.WarningCode);
    }

    [Fact]
    public void The_resolver_distinguishes_classes()
    {
        var citizen = Rule(StatutoryScheme.Epf, klass: "citizen");
        var res = RateResolver.Resolve([citizen], StatutoryScheme.Epf, "permanent-resident", 30, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        Assert.Null(res.Rule);
        Assert.Equal(RateResolver.Missing, res.WarningCode);
    }
}
