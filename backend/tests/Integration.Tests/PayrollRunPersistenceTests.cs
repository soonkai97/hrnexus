using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// The payroll run on a real PostgreSQL: the migration, the encryption of every money figure, the one-run-per-month
/// index, and the version token that refuses a stale save. Each test uses its own throwaway database.
///
/// WHY these tests matter: only a real database proves that salary-derived figures are ciphertext in the column
/// (a leaked backup must not read as a payroll register), that two people cannot start the same month, and that an
/// approval cannot be written over an edit it never saw.
/// </summary>
[Collection("postgres")]
public class PayrollRunPersistenceTests(PostgresFixture fx)
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly PayrollRunBasis Basis = new("1", 26m, 8m, 0.5m, 1);

    private async Task<AppDbContext> FreshAsync()
    {
        var db = fx.NewDbOn($"run_{Guid.NewGuid():N}");
        await db.Database.MigrateAsync();
        return db;
    }

    /// <summary>A run with everything it owns loaded (the run does not auto-include its lines: a list of runs must stay light).</summary>
    private static IQueryable<PayrollRun> Full(AppDbContext db) => db.PayrollRuns
        .Include(r => r.Actors)
        .Include(r => r.Lines).ThenInclude(l => l.Items)
        .Include(r => r.Lines).ThenInclude(l => l.UnpaidDays)
        .Include(r => r.Lines).ThenInclude(l => l.Warnings)
        .AsSplitQuery();

    private static Staff Person(string id, string first)
    {
        var s = Staff.Create(id, first, "Tester", null, "hash", []);
        return s;
    }

    private static PayrollLineResult Result(decimal gross, decimal net, string warning, bool blocking = false) => new(
        gross, net, gross + 300m, 12.34m,
        [
            new PayLine("basic", LineSection.Earning, "Basic salary (prorated)", gross),
            new PayLine("deduction", LineSection.OtherDeduction, "Cash advance", 200m, SourceId: Guid.NewGuid()),
            new PayLine("epf-employee", LineSection.EmployeeStatutory, "EPF employee", 330m, Guid.NewGuid()),
        ],
        [new PayrollWarning("band-gap", warning, blocking)],
        [new PayrollRunUnpaidDayInput(new DateOnly(2026, 10, 5), 0.5m, Guid.NewGuid(), 57.69m)]);

    private static async Task<(PayrollRun Run, Staff Alice)> SeedRunAsync(AppDbContext db, string warning = "Wage 4321.00 is in no band.", bool blocking = false)
    {
        var alice = Person("ALICE1", "Alice");
        var preparer = Person("PREP1", "Prep");
        db.Staff.AddRange(alice, preparer);
        var run = PayrollRun.Start(2026, 10, preparer.Id, Basis, Now);
        run.AddLine(PayrollRunLine.Create(alice.Id, alice.StaffId, alice.FullName), preparer.Id, Now);
        run.SetLineInputs(alice.Id, 111.11m, false, null, preparer.Id, Now);
        run.ApplyResult(alice.Id, Result(4321.50m, 3500.25m, warning, blocking), preparer.Id, Now);
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();
        return (run, alice);
    }

    [Fact]
    public async Task A_run_comes_back_whole_with_its_figures_lines_warnings_unpaid_days_and_history()
    {
        await using var db = await FreshAsync();
        var (run, alice) = await SeedRunAsync(db, blocking: true);
        db.ChangeTracker.Clear();

        var loaded = await Full(db).AsNoTracking().SingleAsync(r => r.Id == run.Id);

        Assert.Equal(PayrollRunState.Draft, loaded.State);
        Assert.Equal((2026, 10, "1", 26m, 8m, 0.5m, 1), (loaded.Year, loaded.Month, loaded.CalculatorVersion, loaded.Divisor, loaded.HoursPerWorkingDay, loaded.DeductionCapRate, loaded.PolicyVersion));
        var line = Assert.Single(loaded.Lines);
        Assert.Equal(alice.Id, line.EmployeeId);
        Assert.Equal("ALICE1", line.StaffId);
        Assert.Equal(111.11m, line.PcbAmount);
        Assert.Equal((4321.50m, 3500.25m, 4621.50m, 12.34m), (line.Gross, line.Net, line.EmployerCost, line.UnappliedDeductions));
        Assert.Equal(["basic", "deduction", "epf-employee"], line.Items.OrderBy(i => i.Order).Select(i => i.Type));
        Assert.Equal(200m, line.Items.Single(i => i.Type == "deduction").Amount);
        Assert.NotNull(line.Items.Single(i => i.Type == "deduction").SourceId);
        Assert.NotNull(line.Items.Single(i => i.Type == "epf-employee").RateRuleId);
        var warning = Assert.Single(line.Warnings);
        Assert.Equal(("band-gap", "Wage 4321.00 is in no band.", true), (warning.Code, warning.Message, warning.Blocking));
        var day = Assert.Single(line.UnpaidDays);
        Assert.Equal((new DateOnly(2026, 10, 5), 0.5m, 57.69m), (day.Date, day.Days, day.DeductionAmount));
        Assert.Contains(loaded.Actors, a => a.Action == PayrollRunAction.Started);
        Assert.Contains(loaded.Actors, a => a.Action == PayrollRunAction.Edited);
    }

    /// <summary>
    /// WHY: a database backup or a stray SELECT must not read as a payroll register. Every figure derived from
    /// salary, the hand-entered tax and the text of a warning (which quotes figures) is ciphertext in its column.
    /// </summary>
    [Fact]
    public async Task Every_money_figure_and_warning_message_is_ciphertext_in_the_database()
    {
        await using var db = await FreshAsync();
        var (run, alice) = await SeedRunAsync(db);
        db.PayItems.Add(PayItem.Create(alice.Id, PayItemKind.Allowance, "Transport", 150.75m, true, true, 0, new DateOnly(2026, 1, 1), null, "admin"));
        var advance = CashAdvance.Create(alice.Id, 900m, new DateOnly(2026, 10, 2), "Rent", "admin");
        db.CashAdvances.Add(advance);
        db.CashAdvanceRepayments.Add(CashAdvanceRepayment.Create(advance.Id, run.Id, 321.45m, Now));
        await db.SaveChangesAsync();

        async Task<string> Raw(string sql) => await db.Database.SqlQueryRaw<string>(sql).SingleAsync();

        var columns = new[]
        {
            (Table: "PayrollRunLines", Column: "Gross", Plain: "4321.5"),
            (Table: "PayrollRunLines", Column: "Net", Plain: "3500.25"),
            (Table: "PayrollRunLines", Column: "EmployerCost", Plain: "4621.5"),
            (Table: "PayrollRunLines", Column: "UnappliedDeductions", Plain: "12.34"),
            (Table: "PayrollRunLines", Column: "PcbAmount", Plain: "111.11"),
            (Table: "PayrollRunLineItems", Column: "Amount", Plain: "4321.5"),
            (Table: "PayrollRunUnpaidDays", Column: "DeductionAmount", Plain: "57.69"),
            (Table: "PayrollRunWarnings", Column: "Message", Plain: "4321.00"),
            (Table: "PayItems", Column: "Amount", Plain: "150.75"),
            (Table: "CashAdvanceRepayments", Column: "Amount", Plain: "321.45"),
        };
        foreach (var c in columns)
        {
            var raw = await Raw($"""SELECT "{c.Column}" AS "Value" FROM "{c.Table}" ORDER BY "{c.Column}" LIMIT 1""");
            Assert.StartsWith("enc1:", raw);
            Assert.DoesNotContain(c.Plain, raw);
        }
        Assert.NotEqual(Guid.Empty, run.Id);
    }

    [Fact]
    public async Task Recalculating_a_person_replaces_their_stored_lines_instead_of_adding_to_them()
    {
        await using var db = await FreshAsync();
        var (run, alice) = await SeedRunAsync(db);
        db.ChangeTracker.Clear();

        var loaded = await Full(db).SingleAsync(r => r.Id == run.Id);
        loaded.ApplyResult(alice.Id, Result(5000m, 4000m, "second"), alice.Id, Now);
        await db.SaveChangesAsync();

        Assert.Equal(3, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "PayrollRunLineItems" """).SingleAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "PayrollRunWarnings" """).SingleAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "PayrollRunUnpaidDays" """).SingleAsync());
    }

    [Fact]
    public async Task Two_runs_for_the_same_month_are_refused_by_the_database()
    {
        await using var db = await FreshAsync();
        var (_, alice) = await SeedRunAsync(db);
        db.PayrollRuns.Add(PayrollRun.Start(2026, 10, alice.Id, Basis, Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// WHY: the approver loaded the run, then the preparer changed a figure. Saving the approval over that edit
    /// would approve numbers the approver never saw; the version token makes the second save fail instead.
    /// </summary>
    [Fact]
    public async Task A_stale_save_is_refused_so_an_approval_cannot_overwrite_an_edit_it_never_saw()
    {
        await using var seed = await FreshAsync();
        var (run, alice) = await SeedRunAsync(seed);
        var dbName = seed.Database.GetDbConnection().Database;
        await using var first = fx.NewDbOn(dbName);
        await using var second = fx.NewDbOn(dbName);
        var a = await Full(first).SingleAsync(r => r.Id == run.Id);
        var b = await Full(second).SingleAsync(r => r.Id == run.Id);

        a.SetLineInputs(alice.Id, 99m, false, null, Guid.NewGuid(), Now);
        await first.SaveChangesAsync();
        b.SetLineInputs(alice.Id, 77m, false, null, Guid.NewGuid(), Now);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_state_change_is_saved_with_its_history_row()
    {
        await using var db = await FreshAsync();
        var (run, _) = await SeedRunAsync(db);
        var tracked = await Full(db).SingleAsync(r => r.Id == run.Id);

        tracked.Submit(Guid.NewGuid(), Now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await Full(db).AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(PayrollRunState.Submitted, loaded.State);
        Assert.Contains(loaded.Actors, a => a.Action == PayrollRunAction.Submitted);
    }

    [Fact]
    public async Task A_sign_off_is_stored_with_who_and_when_and_returning_the_run_clears_it()
    {
        await using var db = await FreshAsync();
        var (run, alice) = await SeedRunAsync(db);
        var manager = Guid.NewGuid();
        var tracked = await Full(db).SingleAsync(r => r.Id == run.Id);
        tracked.Submit(Guid.NewGuid(), Now);
        tracked.SignOff(alice.Id, manager, Now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var signed = (await Full(db).AsNoTracking().SingleAsync(r => r.Id == run.Id)).Lines.Single();
        Assert.Equal(manager, signed.SignedOffBy);
        Assert.NotNull(signed.SignedOffAt);

        var again = await Full(db).SingleAsync(r => r.Id == run.Id);
        again.Return(Guid.NewGuid(), "Check PCB", Now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Null((await Full(db).AsNoTracking().SingleAsync(r => r.Id == run.Id)).Lines.Single().SignedOffBy);
    }

    [Fact]
    public async Task A_person_in_a_run_cannot_be_removed_from_the_database_under_it()
    {
        await using var db = await FreshAsync();
        var (_, alice) = await SeedRunAsync(db);

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("""DELETE FROM "Staff" WHERE "Id" = {0}""", alice.Id));
    }

    [Fact]
    public async Task A_cash_advance_keeps_its_monthly_schedule_and_one_repayment_per_run()
    {
        await using var db = await FreshAsync();
        var (run, alice) = await SeedRunAsync(db);
        var advance = CashAdvance.Create(alice.Id, 500m, new DateOnly(2026, 10, 2), "Rent", "admin");
        advance.SetRepaymentPerMonth(125.50m);
        db.CashAdvances.Add(advance);
        db.CashAdvanceRepayments.Add(CashAdvanceRepayment.Create(advance.Id, run.Id, 125.50m, Now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(125.50m, (await db.CashAdvances.AsNoTracking().SingleAsync(c => c.Id == advance.Id)).RepaymentPerMonth);
        db.CashAdvanceRepayments.Add(CashAdvanceRepayment.Create(advance.Id, run.Id, 10m, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_pay_item_round_trips_with_its_flags_and_validity()
    {
        await using var db = await FreshAsync();
        var (_, alice) = await SeedRunAsync(db);
        var item = PayItem.Create(alice.Id, PayItemKind.Allowance, "Transport", 150.75m, true, false, 0, new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31), "admin");
        db.PayItems.Add(item);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.PayItems.AsNoTracking().SingleAsync(p => p.Id == item.Id);
        Assert.Equal((150.75m, true, false, new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31)),
            (loaded.Amount, loaded.CountsForEpf, loaded.CountsForSocsoEis, loaded.From, loaded.To));
    }

    [Fact]
    public async Task Who_a_person_reports_to_is_stored_and_a_removed_manager_leaves_no_dangling_link()
    {
        await using var db = await FreshAsync();
        var boss = Person("BOSS1", "Boss");
        var worker = Person("WORK1", "Worker");
        worker.SetReportsTo(boss.Id);
        db.Staff.AddRange(boss, worker);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(boss.Id, (await db.Staff.AsNoTracking().SingleAsync(s => s.Id == worker.Id)).ReportsToId);

        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Staff" WHERE "Id" = {0}""", boss.Id);

        Assert.Null((await db.Staff.AsNoTracking().SingleAsync(s => s.Id == worker.Id)).ReportsToId);
    }
}
