using HRNexus.Application.Payroll;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using HRNexus.Infrastructure.Auth;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Statutory rate tables on a real PostgreSQL: the unique index, the owned bands, exact decimal round trips,
/// and the insert-if-missing seed. Each test uses its own throwaway database.
///
/// WHY these tests matter: only a real database proves the unique (scheme, class, age, effective date) index
/// that makes "a new rate is a new row" safe, that bands cascade with their table, and that a rate like 0.0175
/// comes back exactly (a rounding drift in storage would silently misprice every payslip).
/// </summary>
[Collection("postgres")]
public class StatutoryRatePersistenceTests(PostgresFixture fx)
{
    private async Task<AppDbContext> FreshAsync()
    {
        var db = fx.NewDbOn($"rates_{Guid.NewGuid():N}");
        await db.Database.MigrateAsync();
        return db;
    }

    private static Task SeedAsync(AppDbContext db) => SeedData.SeedDemoUsersAsync(db, new PasswordHasher());

    [Fact]
    public async Task A_fresh_install_seeds_the_twelve_unverified_rows_and_seeding_twice_adds_none()
    {
        await using var db = await FreshAsync();
        await SeedAsync(db);
        await SeedAsync(db);

        var rows = await db.StatutoryRateTables.AsNoTracking().ToListAsync();
        Assert.Equal(12, rows.Count);
        Assert.All(rows, r => Assert.False(r.Verified));
        Assert.Single(rows, r => r.Scheme == StatutoryScheme.MinimumWage && r.CitizenshipClass == "citizen");
    }

    [Fact]
    public async Task A_verified_row_is_never_overwritten_by_the_next_start()
    {
        await using var db = await FreshAsync();
        await SeedAsync(db);
        var epf = await db.StatutoryRateTables.FirstAsync(r => r.Scheme == StatutoryScheme.Epf && r.CitizenshipClass == "citizen");
        epf.Verify("accountant", DateTime.UtcNow, "checked against the Third Schedule");
        await db.SaveChangesAsync();

        await SeedAsync(db);   // the next start

        db.ChangeTracker.Clear();
        var again = await db.StatutoryRateTables.AsNoTracking().FirstAsync(r => r.Id == epf.Id);
        Assert.True(again.Verified);
        Assert.Equal("accountant", again.VerifiedBy);
    }

    [Fact]
    public async Task Rates_round_trip_exactly_including_the_four_decimal_places()
    {
        await using var db = await FreshAsync();
        await SeedAsync(db);
        db.ChangeTracker.Clear();

        var socso = await db.StatutoryRateTables.AsNoTracking().FirstAsync(r => r.Scheme == StatutoryScheme.Socso && r.CitizenshipClass == "citizen");
        Assert.Equal(0.0175m, socso.EmployerRate);
        Assert.Equal(0.005m, socso.EmployeeRate);
        Assert.Equal(6000.00m, socso.WageCeiling);
        Assert.Equal(RoundingRule.Nearest5Sen, socso.Rounding);
        Assert.Equal(new DateOnly(2024, 10, 1), socso.EffectiveFrom);
    }

    [Fact]
    public async Task Two_rows_for_the_same_scheme_class_age_and_date_are_refused_by_the_database()
    {
        await using var db = await FreshAsync();
        await SeedAsync(db);
        var epf = await db.StatutoryRateTables.AsNoTracking().FirstAsync(r => r.Scheme == StatutoryScheme.Epf && r.CitizenshipClass == "citizen");
        db.StatutoryRateTables.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", epf.EffectiveFrom, 0.12m, 0.14m, RoundingRule.None, "duplicate"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_new_rate_is_a_new_row_with_a_later_date_and_it_resolves_for_later_months_only()
    {
        await using var db = await FreshAsync();
        await SeedAsync(db);
        db.StatutoryRateTables.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, "citizen", new DateOnly(2027, 1, 1),
            0.12m, 0.14m, RoundingRule.UpToNextRinggit, "a later rate"));
        await db.SaveChangesAsync();
        var rules = await StatutoryRates.LoadRulesAsync(db, CancellationToken.None);

        var june2026 = RateResolver.Resolve(rules, StatutoryScheme.Epf, "citizen", 30, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var jan2027 = RateResolver.Resolve(rules, StatutoryScheme.Epf, "citizen", 30, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31));

        Assert.Equal(0.11m, june2026.Rule!.EmployeeRate);
        Assert.Equal(0.12m, jan2027.Rule!.EmployeeRate);
    }

    [Fact]
    public async Task Bands_are_stored_with_their_table_and_removed_with_it()
    {
        await using var db = await FreshAsync();
        var table = StatutoryRateTable.CreateBandTable(StatutoryScheme.Socso, "citizen", new DateOnly(2026, 1, 1),
            [new RateBandRule(0m, 1000m, 10.05m, 20.10m), new RateBandRule(1000m, 2000m, 20.15m, 40.20m)], RoundingRule.None, "test table");
        db.StatutoryRateTables.Add(table);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == table.Id);
        Assert.Equal(new[] { 10.05m, 20.15m }, loaded.Bands.OrderBy(b => b.WageFrom).Select(b => b.EmployeeAmount).ToArray());
        Assert.Equal(40.20m, loaded.ToRule().Bands[1].EmployerAmount);

        db.StatutoryRateTables.Remove(await db.StatutoryRateTables.SingleAsync(t => t.Id == table.Id));
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "StatutoryRateBands" """).SingleAsync());
    }

    [Fact]
    public async Task The_guard_works_against_the_real_database_and_clears_once_rows_are_verified()
    {
        await using var db = await FreshAsync();
        await SeedAsync(db);

        var before = await StatutoryRates.UnverifiedAsync(db, 2026, 6, [("citizen", 30)], CancellationToken.None);
        Assert.Equal(4, before.Count);

        foreach (var t in await db.StatutoryRateTables.Where(t => t.CitizenshipClass == "citizen").ToListAsync())
            t.Verify("accountant", DateTime.UtcNow, "ok");
        await db.SaveChangesAsync();

        Assert.Empty(await StatutoryRates.UnverifiedAsync(db, 2026, 6, [("citizen", 30)], CancellationToken.None));
    }
}
