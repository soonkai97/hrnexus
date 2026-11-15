using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Starting a month's payroll run, changing its inputs, calculating it again and deleting it while it is still a draft.
///
/// WHY these tests matter: a run is the single place a month's pay is decided. It must start only when it cannot
/// collide with another open run (two open runs would deduct the same cash advance twice), must refuse a stale edit
/// (two preparers overwriting each other), and must never leave an audit row that says what anyone was paid.
/// </summary>
public class PayrollRunDraftCommandTests
{
    private static Task<Guid> Start(PayrollRunKit k, E.Staff by, int year = PayrollRunKit.Year, int month = PayrollRunKit.Month) =>
        new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(year, month, PayrollRunKit.Act(by)), CancellationToken.None);

    private static Task<long> Edit(PayrollRunKit k, Guid runId, E.Staff by, Guid employee, decimal? pcb, bool excluded = false, string? reason = null, long? version = null) =>
        new UpdatePayrollRunLineCommandHandler(k.Db, k.Audit, k.Clock).Handle(
            new UpdatePayrollRunLineCommand(runId, employee, pcb, excluded, reason, version ?? k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version, PayrollRunKit.Act(by)),
            CancellationToken.None);

    private static async Task<PayrollRun> RunAsync(PayrollRunKit k, Guid id) =>
        await PayrollRunWorkForTests.LoadAsync(k.Db, id);

    // ── start ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Starting_a_month_makes_a_draft_with_everyone_who_works_in_it_already_calculated()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE", 3000m);
        var bob = await k.PersonAsync("BOB", 4000m);

        var id = await Start(k, prep);

        var run = await RunAsync(k, id);
        Assert.Equal((PayrollRunState.Draft, 2026, 10), (run.State, run.Year, run.Month));
        Assert.Equal(["ALICE", "BOB"], run.Lines.Select(l => l.StaffId).Order());
        Assert.All(run.Lines, l => Assert.True(l.Calculated));
        Assert.Equal(3000m, run.LineFor(alice.Id).Gross);
        Assert.Equal(4000m, run.LineFor(bob.Id).Gross);
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Started && a.StaffId == prep.Id);
    }

    [Fact]
    public async Task A_new_run_records_the_settings_it_was_calculated_with()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        await k.PersonAsync("ALICE");

        var run = await RunAsync(k, await Start(k, await k.PreparerAsync()));

        Assert.Equal(("1", 26m, 8m, 0.5m), (run.CalculatorVersion, run.Divisor, run.HoursPerWorkingDay, run.DeductionCapRate));
    }

    [Fact]
    public async Task A_month_cannot_have_two_runs()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        await Start(k, prep);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Start(k, prep));

        Assert.Contains("already exists", ex.Message);
    }

    /// <summary>WHY: two open runs would each deduct the same cash advance. Only one run is open at a time.</summary>
    [Fact]
    public async Task A_month_cannot_be_started_while_an_earlier_run_is_not_locked()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        await Start(k, prep, 2026, 9);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Start(k, prep, 2026, 10));

        Assert.Contains("2026-09", ex.Message);
        Assert.Contains("not locked", ex.Message);
    }

    /// <summary>WHY: the open-run rule is about any open run, not only earlier ones: a later draft already scheduled the same advances.</summary>
    [Fact]
    public async Task An_earlier_month_cannot_be_started_while_a_later_run_is_still_open()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        await Start(k, prep, 2026, 11);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Start(k, prep, 2026, 10));

        Assert.Contains("2026-11", ex.Message);
    }

    [Fact]
    public async Task The_first_run_ever_may_start_in_any_month_because_nothing_earlier_is_open()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        await k.PersonAsync("ALICE");

        Assert.NotEqual(Guid.Empty, await Start(k, await k.PreparerAsync(), 2026, 10));
    }

    [Theory]
    [InlineData(2026, 12, true)]    // next month is fine (now is mid-November)
    [InlineData(2027, 1, false)]    // two months ahead is not
    public async Task A_run_can_start_for_next_month_but_no_further_ahead(int year, int month, bool allowed)
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        await k.PersonAsync("ALICE");
        var prep = await k.PreparerAsync();

        if (allowed) Assert.NotEqual(Guid.Empty, await Start(k, prep, year, month));
        else await Assert.ThrowsAsync<ArgumentException>(() => Start(k, prep, year, month));
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 5)]
    public async Task A_month_that_does_not_exist_is_refused(int year, int month)
    {
        var k = new PayrollRunKit();

        var prep = await k.PreparerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Start(k, prep, year, month));
    }

    [Fact]
    public async Task A_month_with_nobody_to_pay_is_refused()
    {
        var k = new PayrollRunKit();

        var prep = await k.PreparerAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Start(k, prep));

        Assert.Contains("Nobody", ex.Message);
    }

    /// <summary>WHY: the audit trail must say that a run started, which month and who, and never what anyone earns.</summary>
    [Fact]
    public async Task Starting_writes_an_audit_row_with_the_month_and_the_person_and_no_figures()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE", 3000m);

        var id = await Start(k, prep);

        var row = Assert.Single(k.Audit.Entries);
        Assert.Equal(("PayrollRun", id.ToString(), "Started", (Guid?)prep.Id, "2026-10"), (row.Type, row.Id, row.Action, row.By, row.Changes));
    }

    // ── editing the inputs ────────────────────────────────────────────────────────

    [Fact]
    public async Task Setting_the_pcb_calculates_that_person_again_and_returns_the_new_version()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE", 3000m);
        var id = await Start(k, prep);
        var netBefore = (await RunAsync(k, id)).LineFor(alice.Id).Net;
        var versionBefore = (await RunAsync(k, id)).Version;

        var version = await Edit(k, id, prep, alice.Id, 150m);

        var line = (await RunAsync(k, id)).LineFor(alice.Id);
        Assert.Equal(150m, line.PcbAmount);
        Assert.Equal(netBefore - 150m, line.Net);
        Assert.True(version > versionBefore);
        Assert.DoesNotContain(line.Warnings, w => w.Code == "no-pcb-entered");
    }

    /// <summary>WHY: two preparers open the same run; the second save must not silently overwrite the first.</summary>
    [Fact]
    public async Task An_edit_made_from_a_stale_copy_is_refused()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var id = await Start(k, prep);
        var staleVersion = (await RunAsync(k, id)).Version;
        await Edit(k, id, prep, alice.Id, 100m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Edit(k, id, prep, alice.Id, 200m, version: staleVersion));

        Assert.Contains("changed by someone else", ex.Message);
        Assert.Equal(100m, (await RunAsync(k, id)).LineFor(alice.Id).PcbAmount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000001)]
    public async Task A_pcb_that_is_negative_or_absurd_is_refused(int pcb)
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var id = await Start(k, prep);

        await Assert.ThrowsAsync<ArgumentException>(() => Edit(k, id, prep, alice.Id, pcb));
    }

    [Fact]
    public async Task A_pcb_with_more_than_two_decimals_is_refused()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var id = await Start(k, prep);

        await Assert.ThrowsAsync<ArgumentException>(() => Edit(k, id, prep, alice.Id, 10.005m));
    }

    [Fact]
    public async Task Leaving_a_person_out_needs_a_reason_and_keeps_them_listed()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var id = await Start(k, prep);

        await Assert.ThrowsAsync<ArgumentException>(() => Edit(k, id, prep, alice.Id, null, excluded: true, reason: " "));
        await Edit(k, id, prep, alice.Id, null, excluded: true, reason: "Final pay handled separately");

        var line = (await RunAsync(k, id)).LineFor(alice.Id);
        Assert.True(line.Excluded);
        Assert.Equal("Final pay handled separately", line.ExcludedReason);
    }

    [Fact]
    public async Task Putting_a_person_back_in_calculates_them_again()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var id = await Start(k, prep);
        await Edit(k, id, prep, alice.Id, null, excluded: true, reason: "Away");

        await Edit(k, id, prep, alice.Id, null, excluded: false);

        var line = (await RunAsync(k, id)).LineFor(alice.Id);
        Assert.False(line.Excluded);
        Assert.True(line.Calculated);
    }

    [Fact]
    public async Task Editing_a_person_who_is_not_in_the_run_is_refused()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        var id = await Start(k, prep);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Edit(k, id, prep, Guid.NewGuid(), 10m));
    }

    [Fact]
    public async Task Editing_a_run_that_does_not_exist_is_a_not_found()
    {
        var k = new PayrollRunKit();

        var prep = await k.PreparerAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Edit(k, Guid.NewGuid(), prep, Guid.NewGuid(), 1m, version: 0));
    }

    /// <summary>WHY: no audit row may carry a figure. A PCB edit says which person's line changed, nothing more.</summary>
    [Fact]
    public async Task An_edit_is_audited_with_the_person_but_not_the_amount()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var id = await Start(k, prep);

        await Edit(k, id, prep, alice.Id, 123.45m);

        var row = Assert.Single(k.Audit.Entries, e => e.Action == "LineEdited");
        Assert.Equal("2026-10 ALICE", row.Changes);
        Assert.DoesNotContain("123", row.Changes);
    }

    // ── recalculating ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recalculating_picks_up_a_new_leave_and_keeps_the_inputs_typed_in()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE", 2600m);
        var id = await Start(k, prep);
        await Edit(k, id, prep, alice.Id, 80m);
        var leave = LeaveRequest.Create(alice.Id, "unpaid", new DateTime(2026, 10, 7), new DateTime(2026, 10, 7), null, null, 8m, null);
        leave.Update("unpaid", new DateTime(2026, 10, 7), new DateTime(2026, 10, 7), null, null, 8m, null, "approved");
        k.Db.LeaveRequests.Add(leave);
        await k.Db.SaveChangesAsync();

        var version = (await RunAsync(k, id)).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new RecalculatePayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        var line = (await RunAsync(k, id)).LineFor(alice.Id);
        Assert.Equal(2500m, line.Gross);            // 2600 - 2600/26 for the unpaid day
        Assert.Equal(80m, line.PcbAmount);
        Assert.Single(line.UnpaidDays);
    }

    [Fact]
    public async Task Recalculating_adds_someone_who_became_eligible_after_the_run_started()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        var id = await Start(k, prep);
        var late = await k.PersonAsync("LATE");

        var version = (await RunAsync(k, id)).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new RecalculatePayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        var run = await RunAsync(k, id);
        Assert.Equal(["ALICE", "LATE"], run.Lines.Select(l => l.StaffId).Order());
        Assert.True(run.LineFor(late.Id).Calculated);
    }

    [Fact]
    public async Task Recalculating_from_a_stale_copy_is_refused()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        var id = await Start(k, prep);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new RecalculatePayrollRunCommand(id, -5, PayrollRunKit.Act(prep)), CancellationToken.None));
    }

    /// <summary>WHY: a person who lost their salary row cannot be calculated; the run must say so instead of keeping last week's figure.</summary>
    [Fact]
    public async Task Recalculating_stops_when_someone_in_the_run_can_no_longer_be_calculated()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var id = await Start(k, prep);
        k.Db.SalaryProfiles.RemoveRange(k.Db.SalaryProfiles.Where(p => p.EmployeeId == alice.Id));
        await k.Db.SaveChangesAsync();

        var version = (await RunAsync(k, id)).Version;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new RecalculatePayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None));

        Assert.Contains("ALICE", ex.Message);
    }

    [Fact]
    public async Task Recalculating_records_the_new_company_settings_as_the_basis()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        var id = await Start(k, prep);
        var policy = CompanyPolicy.Default();
        policy.Update([1, 2, 3, 4, 5], 8m, 3, 30, "capped", 14m, 30m, "admin");
        k.Db.CompanyPolicies.Add(policy);
        await k.Db.SaveChangesAsync();

        var version = (await RunAsync(k, id)).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new RecalculatePayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        Assert.Equal(30m, (await RunAsync(k, id)).Divisor);
    }

    // ── delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_draft_run_can_be_deleted_with_everything_in_it()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        var id = await Start(k, prep);

        await new DeletePayrollRunCommandHandler(k.Db, k.Audit).Handle(new DeletePayrollRunCommand(id, PayrollRunKit.Act(prep)), CancellationToken.None);

        Assert.False(await k.Db.PayrollRuns.AnyAsync());
        Assert.Contains(k.Audit.Entries, e => e.Action == "Deleted");
    }

    /// <summary>WHY: a run that was returned or reopened has history, and may have been paid; deleting it would erase that.</summary>
    [Fact]
    public async Task A_run_that_was_returned_or_reopened_cannot_be_deleted()
    {
        var run = PayrollRun.Start(2026, 10, Guid.NewGuid(), new PayrollRunBasis("1", 26m, 8m, 0.5m, 0), DateTime.UtcNow);
        run.AddLine(PayrollRunLine.Create(Guid.NewGuid(), "A", "A"), Guid.NewGuid(), DateTime.UtcNow);
        run.ApplyResult(run.Lines[0].EmployeeId, new PayrollLineResult(3000m, 2500m, 3300m, 0m, [], [], []), Guid.NewGuid(), DateTime.UtcNow);
        run.Submit(Guid.NewGuid(), DateTime.UtcNow);
        run.Return(Guid.NewGuid(), "again", DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(run.EnsureCanDelete);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_run_that_is_not_a_draft_cannot_be_deleted()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("ALICE");
        var id = await Start(k, prep);
        var version = (await RunAsync(k, id)).Version;
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DeletePayrollRunCommandHandler(k.Db, k.Audit)
            .Handle(new DeletePayrollRunCommand(id, PayrollRunKit.Act(prep)), CancellationToken.None));
        Assert.True(await k.Db.PayrollRuns.AnyAsync());
    }
}

/// <summary>Loads a run the way the commands do, for assertions.</summary>
internal static class PayrollRunWorkForTests
{
    public static async Task<PayrollRun> LoadAsync(HRNexus.Application.Common.Interfaces.IAppDbContext db, Guid id)
    {
        var run = await db.PayrollRuns.AsNoTracking().Include(r => r.Actors).Include(r => r.Lines).SingleAsync(r => r.Id == id);
        return run;
    }
}
