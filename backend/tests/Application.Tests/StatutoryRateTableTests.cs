using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// The statutory rate table is an aggregate: it protects its own rules so that no caller can store a rate row
/// the calculator could misread.
///
/// WHY these tests matter: a rate row, once stored and verified by an accountant, must never change under
/// anyone's feet (a past month has to be recalculable exactly), and a malformed row (rate above 100%,
/// overlapping bands, a threshold with no "above" rate) would silently misprice every payslip that uses it.
/// </summary>
public class StatutoryRateTableTests
{
    private static readonly DateOnly From = new(2025, 1, 1);

    private static StatutoryRateTable Epf(decimal ee = 0.11m, decimal er = 0.13m) =>
        StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", From, ee, er, RoundingRule.UpToNextRinggit, "PRD 6.2",
            mode: EmployerRateMode.WholeWageByThreshold, threshold: 5000m, above: 0.12m);

    // ── creation rules ────────────────────────────────────────────────────────────

    [Fact]
    public void A_new_row_starts_unverified_and_keeps_every_value_it_was_created_with()
    {
        var t = Epf();
        Assert.False(t.Verified);
        Assert.Null(t.VerifiedBy);
        Assert.Equal(StatutoryScheme.Epf, t.Scheme);
        Assert.Equal("citizen", t.CitizenshipClass);
        Assert.Equal(0.11m, t.EmployeeRate);
        Assert.Equal(EmployerRateMode.WholeWageByThreshold, t.EmployerRateMode);
        Assert.Equal(5000m, t.EmployerRateThreshold);
        Assert.Equal(0.12m, t.EmployerRateAbove);
        Assert.Equal(RoundingRule.UpToNextRinggit, t.Rounding);
        Assert.Equal("PRD 6.2", t.Source);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    public void An_unknown_scheme_is_refused(string scheme) =>
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(scheme, "citizen", From, 0.1m, 0.1m, RoundingRule.None, "x"));

    [Fact]
    public void Only_citizens_and_permanent_residents_have_rows()
    {
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "foreign", From, 0.1m, 0.1m, RoundingRule.None, "x"));
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "all", From, 0.1m, 0.1m, RoundingRule.None, "x"));
    }

    [Theory]
    [InlineData(-0.01, 0.1)]
    [InlineData(0.1, 1.01)]
    public void A_rate_outside_0_to_100_percent_is_refused(double ee, double er) =>
        Assert.Throws<ArgumentException>(() => Epf((decimal)ee, (decimal)er));

    [Fact]
    public void A_source_is_required_so_every_figure_says_where_it_came_from() =>
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", From, 0.1m, 0.1m, RoundingRule.None, " "));

    [Fact]
    public void A_threshold_rule_needs_both_the_threshold_and_the_rate_above_it()
    {
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", From, 0.11m, 0.13m, RoundingRule.None, "x",
                mode: EmployerRateMode.WholeWageByThreshold, threshold: null, above: 0.12m));
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", From, 0.11m, 0.13m, RoundingRule.None, "x",
                mode: EmployerRateMode.WholeWageByThreshold, threshold: 5000m, above: null));
    }

    [Fact]
    public void A_ceiling_must_be_more_than_zero() =>
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Socso, "citizen", From, 0.005m, 0.0175m, RoundingRule.None, "x", ceiling: 0m));

    [Fact]
    public void The_age_range_must_make_sense()
    {
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", From, 0.1m, 0.1m, RoundingRule.None, "x", ageFrom: 60, ageTo: 50));
        Assert.Throws<ArgumentException>(() =>
            StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", From, 0.1m, 0.1m, RoundingRule.None, "x", ageFrom: -1));
    }

    // ── band tables ───────────────────────────────────────────────────────────────

    private static StatutoryRateTable Bands(params (decimal from, decimal to, decimal ee, decimal er)[] bands) =>
        StatutoryRateTable.CreateBandTable(StatutoryScheme.Socso, "citizen", From, bands.Select(b => new RateBandRule(b.from, b.to, b.ee, b.er)).ToList(), RoundingRule.None, "PERKESO");

    [Fact]
    public void A_band_table_keeps_its_bands_in_wage_order()
    {
        var t = Bands((1000, 2000, 20, 40), (0, 1000, 10, 20));
        Assert.Equal([0m, 1000m], t.Bands.Select(b => b.WageFrom));
    }

    [Fact]
    public void A_band_table_needs_at_least_one_band() =>
        Assert.Throws<ArgumentException>(() => Bands());

    [Fact]
    public void Bands_must_not_overlap_and_must_each_span_a_range()
    {
        Assert.Throws<ArgumentException>(() => Bands((0, 1000, 10, 20), (900, 2000, 20, 40)));
        Assert.Throws<ArgumentException>(() => Bands((1000, 1000, 10, 20)));
        Assert.Throws<ArgumentException>(() => Bands((-5, 1000, 10, 20)));
        Assert.Throws<ArgumentException>(() => Bands((0, 1000, -1, 20)));
    }

    [Fact]
    public void A_gap_between_bands_is_allowed_to_be_stored_the_calculator_flags_it() =>
        Assert.Equal(2, Bands((0, 1000, 10, 20), (1200, 2000, 20, 40)).Bands.Count);

    // ── minimum wage ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_minimum_wage_row_carries_an_amount_not_a_rate()
    {
        var t = StatutoryRateTable.CreateMinimumWage("citizen", new DateOnly(2025, 2, 1), 1700m, "PRD 6.2");
        Assert.Equal(StatutoryScheme.MinimumWage, t.Scheme);
        Assert.Equal(1700m, t.Amount);
        Assert.Throws<ArgumentException>(() => StatutoryRateTable.CreateMinimumWage("citizen", From, 0m, "x"));
    }

    // ── verification and the active switch: the ONLY things that change ───────────

    [Fact]
    public void Verifying_records_who_when_and_why()
    {
        var t = Epf();
        var at = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
        t.Verify("accountant", at, "Checked against the KWSP Third Schedule");
        Assert.True(t.Verified);
        Assert.Equal("accountant", t.VerifiedBy);
        Assert.Equal(at, t.VerifiedAt);
        Assert.Equal("Checked against the KWSP Third Schedule", t.VerificationNote);
    }

    [Theory]
    [InlineData("", "note")]
    [InlineData("accountant", "")]
    [InlineData("accountant", "  ")]
    public void Verifying_needs_a_verifier_and_a_note(string by, string note) =>
        Assert.Throws<ArgumentException>(() => Epf().Verify(by, DateTime.UtcNow, note));

    [Fact]
    public void A_verified_row_cannot_be_verified_again()
    {
        var t = Epf();
        t.Verify("a", DateTime.UtcNow, "ok");
        Assert.Throws<InvalidOperationException>(() => t.Verify("b", DateTime.UtcNow, "again"));
        Assert.Equal("a", t.VerifiedBy);   // the first verification stands
    }

    [Fact]
    public void The_active_switch_toggles_and_nothing_else_about_the_row_changes()
    {
        var t = StatutoryRateTable.CreatePercentage(StatutoryScheme.HrdCompulsory, "citizen", From, 0m, 0.01m, RoundingRule.NearestCent, "PRD", active: false);
        Assert.False(t.Active);
        t.SetActive(true);
        Assert.True(t.Active);
        Assert.Equal(0.01m, t.EmployerRate);
        Assert.False(t.Verified);
    }

    [Fact]
    public void The_rate_values_have_no_public_setters_so_a_row_cannot_be_edited_after_creation()
    {
        var settable = typeof(StatutoryRateTable).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name).ToList();
        Assert.Empty(settable);
    }

    // ── mapping to the calculator's plain input ───────────────────────────────────

    [Fact]
    public void A_row_maps_to_the_calculators_rule_without_losing_anything()
    {
        var t = Epf();
        t.Verify("a", DateTime.UtcNow, "ok");
        var r = t.ToRule();
        Assert.Equal(t.Id, r.Id);
        Assert.Equal(StatutoryScheme.Epf, r.Scheme);
        Assert.Equal("citizen", r.CitizenshipClass);
        Assert.Equal(From, r.EffectiveFrom);
        Assert.Equal(0.11m, r.EmployeeRate);
        Assert.Equal(EmployerRateMode.WholeWageByThreshold, r.EmployerRateMode);
        Assert.Equal(5000m, r.EmployerRateThreshold);
        Assert.Equal(0.12m, r.EmployerRateAbove);
        Assert.True(r.Verified);
    }

    [Fact]
    public void A_band_table_maps_its_bands()
    {
        var r = Bands((0, 1000, 10, 20), (1000, 2000, 20, 40)).ToRule();
        Assert.Equal(RateKind.BandTable, r.Kind);
        Assert.Equal(2, r.Bands.Count);
        Assert.Equal(40m, r.Bands[1].EmployerAmount);
    }

    // ── the seed rows (PRD 6.2), all unverified ───────────────────────────────────

    [Fact]
    public void The_seed_has_one_row_per_scheme_and_class_and_every_row_is_unverified()
    {
        var seed = StatutoryRateSeed.Rows();
        Assert.Equal(12, seed.Count);   // 6 schemes x 2 classes
        Assert.All(seed, r => Assert.False(r.Verified));
        Assert.All(seed, r => Assert.False(string.IsNullOrWhiteSpace(r.Source)));
        foreach (var scheme in StatutoryScheme.All)
            Assert.Equal(2, seed.Count(r => r.Scheme == scheme));
    }

    [Fact]
    public void The_owners_answer_is_in_the_seed_compulsory_HRD_on_optional_off()
    {
        var seed = StatutoryRateSeed.Rows();
        Assert.All(seed.Where(r => r.Scheme == StatutoryScheme.HrdCompulsory), r => { Assert.True(r.Active); Assert.Equal(0.01m, r.EmployerRate); });
        Assert.All(seed.Where(r => r.Scheme == StatutoryScheme.HrdOptional), r => { Assert.False(r.Active); Assert.Equal(0.005m, r.EmployerRate); });
    }

    [Fact]
    public void The_seed_matches_the_PRD_rows()
    {
        var epf = StatutoryRateSeed.Rows().Single(r => r.Scheme == StatutoryScheme.Epf && r.CitizenshipClass == "citizen");
        Assert.Equal(0.11m, epf.EmployeeRate);
        Assert.Equal(0.13m, epf.EmployerRate);
        Assert.Equal(5000m, epf.EmployerRateThreshold);
        Assert.Equal(0.12m, epf.EmployerRateAbove);

        var socso = StatutoryRateSeed.Rows().Single(r => r.Scheme == StatutoryScheme.Socso && r.CitizenshipClass == "citizen");
        Assert.Equal(6000m, socso.WageCeiling);
        Assert.Equal(0.005m, socso.EmployeeRate);
        Assert.Equal(0.0175m, socso.EmployerRate);

        var min = StatutoryRateSeed.Rows().Single(r => r.Scheme == StatutoryScheme.MinimumWage && r.CitizenshipClass == "citizen");
        Assert.Equal(1700m, min.Amount);
        Assert.Equal(new DateOnly(2025, 2, 1), min.EffectiveFrom);
    }

    [Fact]
    public void The_seed_notes_say_the_percentage_rows_are_placeholders()
    {
        var epf = StatutoryRateSeed.Rows().First(r => r.Scheme == StatutoryScheme.Epf);
        Assert.Contains("placeholder", epf.Note, StringComparison.OrdinalIgnoreCase);
    }
}
