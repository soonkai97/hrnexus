using HRNexus.Application.Leave;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Leave;
using HRNexus.Domain.Policy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// How handlers read the leave types (Phase 7q): the code defaults until a catalogue is saved, then the
/// saved rows, and the two derived facts every leave calculation needs (the schedule a type counts days
/// by, and the wording that never names a sensitive type).
///
/// WHY these tests matter: every existing leave test seeds no catalogue table and must keep passing, so
/// "no saved rows means today's behaviour" is the parity guarantee; and a sensitive type's name reaching a
/// lock screen is a privacy leak, so the wording rule is pinned here once and used everywhere.
/// </summary>
public class LeaveCatalogTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task With_nothing_saved_the_code_defaults_apply()
    {
        await using var db = CreateDb();

        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);

        Assert.Equal(LeaveTypeDefaults.All().Select(t => t.Key), catalog.Types.Select(t => t.Key));
        Assert.False(catalog.IsSaved);
    }

    [Fact]
    public async Task Once_a_catalogue_is_saved_only_its_rows_apply_in_sort_order()
    {
        await using var db = CreateDb();
        var all = LeaveTypeDefaults.All().ToList();
        // A saved catalogue that renames sick and puts it first.
        var sick = all.Single(t => t.Key == LeaveTypeDefaults.SickKey);
        sick.Update(new LeaveTypeSpec("sick", "Medical", "danger", 1, true, "sick", null, null, true, null, false, false, false, false, false), false, "a");
        db.LeaveTypes.AddRange(all.Where(t => t.Key is "sick" or "no-show" or "annual"));
        await db.SaveChangesAsync();

        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);

        Assert.True(catalog.IsSaved);
        Assert.Equal(["sick", "annual", "no-show"], catalog.Types.Select(t => t.Key));
        Assert.Equal("Medical", catalog.Find("sick")!.Label);
        Assert.Null(catalog.Find("maternity"));   // not in the saved catalogue
    }

    [Fact]
    public async Task A_type_is_found_by_its_key_whatever_the_case()
    {
        await using var db = CreateDb();
        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);

        Assert.Equal("annual", catalog.Find("ANNUAL")!.Key);   // the stored key is lower case; clients may send any case
        Assert.Null(catalog.Find("nonsense"));
    }

    [Fact]
    public async Task Types_sharing_a_pool_are_charged_together_and_an_own_pool_is_the_type_alone()
    {
        await using var db = CreateDb();
        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);

        Assert.Equal(["annual", "emergency"], catalog.PoolKeys(catalog.Find("annual")!).OrderBy(k => k));
        Assert.Equal(["annual", "emergency"], catalog.PoolKeys(catalog.Find("emergency")!).OrderBy(k => k));
        Assert.Equal(["hospitalisation"], catalog.PoolKeys(catalog.Find("hospitalisation")!));
        Assert.Empty(catalog.PoolKeys(catalog.Find("maternity")!));   // no balance, no pool
    }

    [Fact]
    public async Task A_calendar_day_type_counts_every_day_of_the_week_at_the_company_day_length()
    {
        await using var db = CreateDb();
        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);
        var company = WorkSchedule.FromIsoDays([1, 2, 3, 4, 5], 7.5m);

        var working = catalog.Find("annual")!.ScheduleFor(company);
        var calendar = catalog.Find("maternity")!.ScheduleFor(company);

        Assert.Equal(company, working);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], calendar.IsoDays);
        Assert.Equal(7.5m, calendar.WorkdayHours);   // hours per day is a company setting, not per type
    }

    [Fact]
    public async Task Wording_names_an_ordinary_type_and_hides_a_sensitive_one()
    {
        await using var db = CreateDb();
        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);

        Assert.Equal("annual leave", catalog.Phrase("annual"));
        Assert.Equal("leave", catalog.Phrase("maternity"));         // health and family facts are not broadcast
        Assert.Equal("leave", catalog.Phrase("hospitalisation"));
        Assert.Equal("old-type leave", catalog.Phrase("old-type")); // a key no longer in the catalogue is shown as stored
    }

    [Fact]
    public async Task Notice_comes_from_the_policy_for_a_type_that_says_so()
    {
        await using var db = CreateDb();
        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);
        var policy = CompanyPolicy.Default();

        Assert.Equal(3, catalog.Find("annual")!.NoticeDays(policy));
        Assert.Null(catalog.Find("emergency")!.NoticeDays(policy));   // no notice: it is for urgent cases
    }
}
