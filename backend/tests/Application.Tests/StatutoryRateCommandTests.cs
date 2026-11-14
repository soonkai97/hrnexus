using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands;
using HRNexus.Application.Payroll.Queries;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Verifying a statutory rate row and switching the optional HRD Corp rows on or off, plus listing the rows.
///
/// WHY these tests matter: verification is the accountant's sign-off that unlocks payroll approval, so it must
/// record who and why (taken from the signed-in user, never from the request), be atomic with its audit row,
/// and never leak a rate figure into the audit trail. The active switch must not let two HRD Corp levy rows
/// charge the employer twice.
/// </summary>
public class StatutoryRateCommandTests
{
    private sealed class StubUser(string? name) : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => name;
    }

    private sealed class Clock(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow => now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => now;
    }

    /// <summary>Behaves like the real AuditService (it saves by itself) and records what it was given and what
    /// the change tracker held at that moment, so a test can prove the change and the audit row commit together.</summary>
    private sealed class SavingAudit(IAppDbContext db) : IAuditService
    {
        public List<string> Changes { get; } = [];
        public bool EntityWasUnsavedWhenLogged { get; private set; }
        public async Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default)
        {
            Changes.Add($"{et}|{a}|{c}");
            EntityWasUnsavedWhenLogged = ((DbContext)db).ChangeTracker.Entries<StatutoryRateTable>().Any(e => e.State == EntityState.Modified);
            db.AuditLogs.Add(AuditLog.Create(et, ei, a, pid, pn, c));
            await db.SaveChangesAsync(ct);
        }
    }

    private static readonly DateTime Now = new(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(TestDbContext db, Guid epfId)> SeededAsync()
    {
        var db = CreateDb();
        db.StatutoryRateTables.AddRange(StatutoryRateSeed.Rows());
        await db.SaveChangesAsync();
        var epf = await db.StatutoryRateTables.FirstAsync(t => t.Scheme == StatutoryScheme.Epf && t.CitizenshipClass == "citizen");
        return (db, epf.Id);
    }

    private static VerifyStatutoryRateCommandHandler Verifier(TestDbContext db, SavingAudit audit, string? user = "accountant.lim") =>
        new(db, audit, new StubUser(user), new Clock(Now));

    // ── verify ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_records_who_when_and_why_taking_the_person_from_the_signed_in_user()
    {
        var (db, id) = await SeededAsync();
        await using var _ = db;

        await Verifier(db, new SavingAudit(db)).Handle(new VerifyStatutoryRateCommand(id, "Checked against the Third Schedule"), CancellationToken.None);

        var row = await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.True(row.Verified);
        Assert.Equal("accountant.lim", row.VerifiedBy);
        Assert.Equal(Now, row.VerifiedAt);
        Assert.Equal("Checked against the Third Schedule", row.VerificationNote);
    }

    [Fact]
    public async Task Verify_commits_the_row_and_its_audit_entry_in_one_save()
    {
        var (db, id) = await SeededAsync();
        await using var _ = db;
        var audit = new SavingAudit(db);

        await Verifier(db, audit).Handle(new VerifyStatutoryRateCommand(id, "ok"), CancellationToken.None);

        // At the moment the audit row was written the verification was still pending in the same unit of work,
        // so a failure cannot leave "verified" without a trail or a trail without "verified".
        Assert.True(audit.EntityWasUnsavedWhenLogged);
        Assert.Single(audit.Changes);
    }

    [Fact]
    public async Task Verify_audit_names_the_row_but_carries_no_rate_figures()
    {
        var (db, id) = await SeededAsync();
        await using var _ = db;
        var audit = new SavingAudit(db);

        await Verifier(db, audit).Handle(new VerifyStatutoryRateCommand(id, "ok"), CancellationToken.None);

        var row = Assert.Single(audit.Changes);
        Assert.Contains("epf", row);
        Assert.DoesNotContain("0.11", row);
        Assert.DoesNotContain("0.13", row);
        Assert.DoesNotContain("5000", row);
    }

    [Fact]
    public async Task Verify_refuses_an_unknown_row_a_second_verification_and_a_missing_note()
    {
        var (db, id) = await SeededAsync();
        await using var _ = db;

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Verifier(db, new SavingAudit(db)).Handle(new VerifyStatutoryRateCommand(Guid.NewGuid(), "x"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Verifier(db, new SavingAudit(db)).Handle(new VerifyStatutoryRateCommand(id, " "), CancellationToken.None));

        await Verifier(db, new SavingAudit(db)).Handle(new VerifyStatutoryRateCommand(id, "first"), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Verifier(db, new SavingAudit(db), "someone.else").Handle(new VerifyStatutoryRateCommand(id, "again"), CancellationToken.None));
        Assert.Equal("accountant.lim", (await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == id)).VerifiedBy);
    }

    [Fact]
    public async Task Verify_needs_a_signed_in_user_it_never_accepts_a_name_from_elsewhere()
    {
        var (db, id) = await SeededAsync();
        await using var _ = db;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Verifier(db, new SavingAudit(db), user: null).Handle(new VerifyStatutoryRateCommand(id, "ok"), CancellationToken.None));
        Assert.False((await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == id)).Verified);
    }

    // ── active switch ─────────────────────────────────────────────────────────────

    private static SetStatutoryRateActiveCommandHandler Switcher(TestDbContext db) => new(db, new SavingAudit(db));

    [Fact]
    public async Task Only_the_HRD_levy_rows_can_be_switched_on_or_off()
    {
        var (db, epfId) = await SeededAsync();
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Switcher(db).Handle(new SetStatutoryRateActiveCommand(epfId, false), CancellationToken.None));
        Assert.Contains("HRD", ex.Message);
    }

    [Fact]
    public async Task Switching_on_the_optional_HRD_row_while_the_compulsory_one_is_on_is_refused()
    {
        var (db, _) = await SeededAsync();
        await using var __ = db;
        var optional = await db.StatutoryRateTables.FirstAsync(t => t.Scheme == StatutoryScheme.HrdOptional && t.CitizenshipClass == "citizen");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Switcher(db).Handle(new SetStatutoryRateActiveCommand(optional.Id, true), CancellationToken.None));
    }

    [Fact]
    public async Task Switching_the_compulsory_row_off_then_the_optional_one_on_works()
    {
        var (db, _) = await SeededAsync();
        await using var __ = db;
        var compulsory = await db.StatutoryRateTables.FirstAsync(t => t.Scheme == StatutoryScheme.HrdCompulsory && t.CitizenshipClass == "citizen");
        var optional = await db.StatutoryRateTables.FirstAsync(t => t.Scheme == StatutoryScheme.HrdOptional && t.CitizenshipClass == "citizen");

        await Switcher(db).Handle(new SetStatutoryRateActiveCommand(compulsory.Id, false), CancellationToken.None);
        await Switcher(db).Handle(new SetStatutoryRateActiveCommand(optional.Id, true), CancellationToken.None);

        Assert.False((await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == compulsory.Id)).Active);
        Assert.True((await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == optional.Id)).Active);
    }

    [Fact]
    public async Task Switching_a_row_never_changes_its_rates_or_verification()
    {
        var (db, _) = await SeededAsync();
        await using var __ = db;
        var compulsory = await db.StatutoryRateTables.FirstAsync(t => t.Scheme == StatutoryScheme.HrdCompulsory && t.CitizenshipClass == "citizen");

        await Switcher(db).Handle(new SetStatutoryRateActiveCommand(compulsory.Id, false), CancellationToken.None);

        var after = await db.StatutoryRateTables.AsNoTracking().SingleAsync(t => t.Id == compulsory.Id);
        Assert.Equal(0.01m, after.EmployerRate);
        Assert.False(after.Verified);
    }

    // ── list ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_returns_every_row_with_its_verification_state_and_bands_in_wage_order()
    {
        var (db, _) = await SeededAsync();
        await using var __ = db;
        db.StatutoryRateTables.Add(StatutoryRateTable.CreateBandTable(StatutoryScheme.Socso, "citizen", new DateOnly(2026, 1, 1),
            [new RateBandRule(1000m, 2000m, 20m, 40m), new RateBandRule(0m, 1000m, 10m, 20m)], RoundingRule.None, "test"));
        await db.SaveChangesAsync();

        var list = await new GetStatutoryRatesQueryHandler(db).Handle(new GetStatutoryRatesQuery(), CancellationToken.None);

        Assert.Equal(13, list.Count);
        Assert.All(list, r => Assert.False(r.Verified));
        var banded = list.Single(r => r.Kind == "band-table");
        Assert.Equal([0m, 1000m], banded.Bands.Select(b => b.WageFrom));
        Assert.Equal("epf", list.First().Scheme);   // grouped by scheme, newest first within a scheme
    }
}
