using HRNexus.Application.Payroll;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// The verification guard: before a payroll run can be approved (Phase 8c), every rate row its calculation would
/// use must have been verified by the accountant.
///
/// WHY these tests matter: this guard is the only thing standing between an unchecked percentage and a payslip
/// that looks authoritative. It must list exactly the rows a month would really use: too few and an unverified
/// rate slips through to approval, too many and payroll is blocked by rows nobody uses.
/// </summary>
public class StatutoryRatesGuardTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<TestDbContext> SeededAsync()
    {
        var db = CreateDb();
        db.StatutoryRateTables.AddRange(StatutoryRateSeed.Rows());
        await db.SaveChangesAsync();
        return db;
    }

    private static readonly (string?, int)[] OneCitizen = [("citizen", 30)];

    [Fact]
    public async Task All_rows_load_as_calculator_rules_with_their_bands()
    {
        await using var db = await SeededAsync();
        db.StatutoryRateTables.Add(StatutoryRateTable.CreateBandTable(StatutoryScheme.Socso, "citizen", new DateOnly(2026, 1, 1),
            [new RateBandRule(0m, 1000m, 10m, 20m)], RoundingRule.None, "test"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rules = await StatutoryRates.LoadRulesAsync(db, CancellationToken.None);

        Assert.Equal(13, rules.Count);
        Assert.Single(rules.Single(r => r.Kind == RateKind.BandTable).Bands);
    }

    [Fact]
    public async Task A_fresh_install_lists_every_row_a_citizens_month_would_use()
    {
        await using var db = await SeededAsync();

        var unverified = await StatutoryRates.UnverifiedAsync(db, 2026, 6, OneCitizen, CancellationToken.None);

        // EPF, SOCSO, EIS and the switched-on 1% HRD row for citizens. Not the optional HRD row (off) and not the
        // minimum wage (a warning, not a gate), not the permanent-resident rows (nobody here is one).
        Assert.Equal(
            [StatutoryScheme.Eis, StatutoryScheme.Epf, StatutoryScheme.HrdCompulsory, StatutoryScheme.Socso],
            unverified.Select(u => u.Scheme).OrderBy(x => x));
        Assert.All(unverified, u => Assert.Equal("citizen", u.CitizenshipClass));
    }

    [Fact]
    public async Task Verified_rows_drop_off_the_list_and_an_empty_list_means_approval_may_proceed()
    {
        await using var db = await SeededAsync();
        foreach (var t in await db.StatutoryRateTables.Where(t => t.CitizenshipClass == "citizen").ToListAsync())
            t.Verify("accountant", DateTime.UtcNow, "checked");
        await db.SaveChangesAsync();

        Assert.Empty(await StatutoryRates.UnverifiedAsync(db, 2026, 6, OneCitizen, CancellationToken.None));
    }

    [Fact]
    public async Task Only_the_classes_of_the_people_in_the_run_matter()
    {
        await using var db = await SeededAsync();
        foreach (var t in await db.StatutoryRateTables.Where(t => t.CitizenshipClass == "citizen").ToListAsync())
            t.Verify("accountant", DateTime.UtcNow, "checked");
        await db.SaveChangesAsync();

        var withPr = await StatutoryRates.UnverifiedAsync(db, 2026, 6, [("citizen", 30), ("permanent-resident", 40)], CancellationToken.None);

        Assert.NotEmpty(withPr);
        Assert.All(withPr, u => Assert.Equal("permanent-resident", u.CitizenshipClass));
    }

    [Fact]
    public async Task A_row_that_has_not_started_yet_is_not_used_so_it_does_not_block_this_month()
    {
        await using var db = await SeededAsync();
        foreach (var t in await db.StatutoryRateTables.Where(t => t.CitizenshipClass == "citizen").ToListAsync())
            t.Verify("accountant", DateTime.UtcNow, "checked");
        // A new, unverified EPF rate that starts next year.
        db.StatutoryRateTables.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", new DateOnly(2027, 1, 1),
            0.12m, 0.14m, RoundingRule.UpToNextRinggit, "next year's rate"));
        await db.SaveChangesAsync();

        Assert.Empty(await StatutoryRates.UnverifiedAsync(db, 2026, 6, OneCitizen, CancellationToken.None));
        Assert.Single(await StatutoryRates.UnverifiedAsync(db, 2027, 1, OneCitizen, CancellationToken.None));
    }

    [Fact]
    public async Task A_person_with_no_citizenship_class_adds_nothing_the_calculator_flags_them_instead()
    {
        await using var db = await SeededAsync();
        Assert.Empty(await StatutoryRates.UnverifiedAsync(db, 2026, 6, [(null, 30)], CancellationToken.None));
    }

    [Fact]
    public async Task The_optional_HRD_row_counts_once_it_is_switched_on()
    {
        await using var db = await SeededAsync();
        var optional = await db.StatutoryRateTables.FirstAsync(t => t.Scheme == StatutoryScheme.HrdOptional && t.CitizenshipClass == "citizen");
        optional.SetActive(true);
        await db.SaveChangesAsync();

        var unverified = await StatutoryRates.UnverifiedAsync(db, 2026, 6, OneCitizen, CancellationToken.None);

        Assert.Contains(unverified, u => u.Scheme == StatutoryScheme.HrdOptional);
    }
}
