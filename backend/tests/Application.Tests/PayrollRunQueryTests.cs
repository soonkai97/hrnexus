using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Application.Payroll.Queries;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// What the payroll run pages are given: the list, one run in full, and a manager's view of their own team.
///
/// WHY these tests matter: a payroll page is the one screen where a wrong filter leaks everyone's pay. The manager's
/// view must return only the people who report to the signed-in manager, nothing at all for a draft, and never a person
/// who was left out; the list's totals must count only people who are really in the run.
/// </summary>
public class PayrollRunQueryTests
{
    private sealed class Scene
    {
        public required PayrollRunKit K { get; init; }
        public required E.Staff Prep { get; init; }
        public required E.Staff Head { get; init; }
        public required E.Staff Mgr { get; init; }
        public required E.Staff OtherMgr { get; init; }
        public required E.Staff Alice { get; init; }
        public required E.Staff Bob { get; init; }
        public Guid RunId { get; set; }
        public long Version => K.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == RunId).Version;
    }

    private static async Task<Scene> DraftAsync(bool verified = false)
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(verified);
        var prep = await k.PreparerAsync();
        var head = await k.HeadOfHrAsync();
        var mgr = await k.ManagerAsync("MGR", salary: false);
        var other = await k.ManagerAsync("OTHER", salary: false);
        var alice = await k.PersonAsync("ALICE", 3000m, reportsTo: mgr.Id);
        var bob = await k.PersonAsync("BOB", 4000m, reportsTo: other.Id);
        var s = new Scene { K = k, Prep = prep, Head = head, Mgr = mgr, OtherMgr = other, Alice = alice, Bob = bob };
        s.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);
        return s;
    }

    private static async Task SubmitAsync(Scene s) =>
        await new SubmitPayrollRunCommandHandler(s.K.Db, s.K.Audit, s.K.Clock).Handle(new SubmitPayrollRunCommand(s.RunId, s.Version, PayrollRunKit.Act(s.Prep)), CancellationToken.None);

    // ── list ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_totals_count_only_the_people_who_are_in_the_run()
    {
        var s = await DraftAsync();
        await new UpdatePayrollRunLineCommandHandler(s.K.Db, s.K.Audit, s.K.Clock).Handle(
            new UpdatePayrollRunLineCommand(s.RunId, s.Bob.Id, null, true, "Paid separately", s.Version, PayrollRunKit.Act(s.Prep)), CancellationToken.None);

        var run = Assert.Single(await new GetPayrollRunsQueryHandler(s.K.Db).Handle(new GetPayrollRunsQuery(), CancellationToken.None));

        Assert.Equal((2026, 10, "draft", 1, 1), (run.Year, run.Month, run.State, run.People, run.LeftOut));
        Assert.Equal(3000m, run.TotalGross);
    }

    [Fact]
    public async Task The_list_is_newest_month_first()
    {
        var s = await DraftAsync();
        s.K.Db.PayrollRuns.Add(E.PayrollRun.Start(2026, 9, s.Prep.Id, new E.PayrollRunBasis("1", 26m, 8m, 0.5m, 0), DateTime.UtcNow));
        await s.K.Db.SaveChangesAsync();

        var runs = await new GetPayrollRunsQueryHandler(s.K.Db).Handle(new GetPayrollRunsQuery(), CancellationToken.None);

        Assert.Equal([10, 9], runs.Select(r => r.Month));
    }

    // ── detail ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task One_run_comes_back_with_its_lines_items_history_and_the_rates_it_rests_on()
    {
        var s = await DraftAsync(verified: false);

        var d = await new GetPayrollRunQueryHandler(s.K.Db).Handle(new GetPayrollRunQuery(s.RunId), CancellationToken.None);

        Assert.Equal(["ALICE", "BOB"], d.Lines.Select(l => l.StaffId));
        var alice = d.Lines.Single(l => l.StaffId == "ALICE");
        Assert.Equal(3000m, alice.Gross);
        Assert.Contains(alice.Items, i => i.Type == "basic" && i.Amount == 3000m);
        Assert.Contains(alice.Warnings, w => w.Code == "no-pcb-entered");
        var started = Assert.Single(d.Steps, st => st.Action == "started");
        Assert.Equal(("First PREP", s.Prep.Id), (started.Name, started.StaffId));
        Assert.Contains("EPF (citizen)", d.UnverifiedRates);
        Assert.Equal(("1", 26m, 8m), (d.CalculatorVersion, d.Divisor, d.HoursPerWorkingDay));
    }

    [Fact]
    public async Task Once_every_rate_is_verified_the_run_lists_no_unverified_rates()
    {
        var s = await DraftAsync(verified: true);

        var d = await new GetPayrollRunQueryHandler(s.K.Db).Handle(new GetPayrollRunQuery(s.RunId), CancellationToken.None);

        Assert.Empty(d.UnverifiedRates);
    }

    [Fact]
    public async Task A_draft_lists_who_cannot_be_in_it_yet_and_who_is_waiting_for_a_recalculation()
    {
        var s = await DraftAsync();
        await s.K.PersonAsync("NOSAL", salary: false);
        await s.K.PersonAsync("LATE");

        var d = await new GetPayrollRunQueryHandler(s.K.Db).Handle(new GetPayrollRunQuery(s.RunId), CancellationToken.None);

        Assert.Contains(d.NotInRun, n => n.StaffId == "NOSAL" && n.Reason.Contains("salary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(d.NotInRun, n => n.StaffId == "LATE" && n.Reason.Contains("recalculate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_run_past_draft_does_not_work_out_who_is_not_in_it()
    {
        var s = await DraftAsync();
        await s.K.PersonAsync("NOSAL", salary: false);
        await SubmitAsync(s);

        var d = await new GetPayrollRunQueryHandler(s.K.Db).Handle(new GetPayrollRunQuery(s.RunId), CancellationToken.None);

        Assert.Empty(d.NotInRun);
    }

    [Fact]
    public async Task A_sign_off_shows_who_signed()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);
        await new SignOffPayrollLineCommandHandler(s.K.Db, s.K.Audit, s.K.Clock).Handle(
            new SignOffPayrollLineCommand(s.RunId, s.Alice.Id, s.Version, PayrollRunKit.Act(s.Mgr)), CancellationToken.None);

        var d = await new GetPayrollRunQueryHandler(s.K.Db).Handle(new GetPayrollRunQuery(s.RunId), CancellationToken.None);

        var line = d.Lines.Single(l => l.StaffId == "ALICE");
        Assert.Equal((s.Mgr.Id, "First MGR"), (line.SignedOffBy, line.SignedOffByName));
        Assert.Equal(1, d.Summary.SignedOff);
    }

    [Fact]
    public async Task A_run_that_does_not_exist_is_not_found()
    {
        var k = new PayrollRunKit();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new GetPayrollRunQueryHandler(k.Db).Handle(new GetPayrollRunQuery(Guid.NewGuid()), CancellationToken.None));
    }

    // ── a manager's team ──────────────────────────────────────────────────────────

    /// <summary>WHY: a manager sees the pay of their direct reports to sign it, and no one else's.</summary>
    [Fact]
    public async Task A_manager_sees_only_the_lines_of_the_people_who_report_to_them()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);

        var mine = await new GetPayrollTeamQueryHandler(s.K.Db).Handle(new GetPayrollTeamQuery(s.RunId, s.Mgr.Id), CancellationToken.None);
        var theirs = await new GetPayrollTeamQueryHandler(s.K.Db).Handle(new GetPayrollTeamQuery(s.RunId, s.OtherMgr.Id), CancellationToken.None);

        Assert.Equal("ALICE", Assert.Single(mine.Lines).StaffId);
        Assert.Equal("BOB", Assert.Single(theirs.Lines).StaffId);
    }

    [Fact]
    public async Task Someone_with_no_reports_sees_nothing()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);

        var team = await new GetPayrollTeamQueryHandler(s.K.Db).Handle(new GetPayrollTeamQuery(s.RunId, s.Head.Id), CancellationToken.None);

        Assert.Empty(team.Lines);
    }

    [Fact]
    public async Task A_manager_sees_nothing_of_a_draft_run()
    {
        var s = await DraftAsync();

        var team = await new GetPayrollTeamQueryHandler(s.K.Db).Handle(new GetPayrollTeamQuery(s.RunId, s.Mgr.Id), CancellationToken.None);

        Assert.Empty(team.Lines);
        Assert.Equal("draft", team.State);
    }

    [Fact]
    public async Task A_report_who_was_left_out_of_the_run_is_not_shown_to_the_manager()
    {
        var s = await DraftAsync();
        await new UpdatePayrollRunLineCommandHandler(s.K.Db, s.K.Audit, s.K.Clock).Handle(
            new UpdatePayrollRunLineCommand(s.RunId, s.Alice.Id, null, true, "Paid separately", s.Version, PayrollRunKit.Act(s.Prep)), CancellationToken.None);
        await SubmitAsync(s);

        var team = await new GetPayrollTeamQueryHandler(s.K.Db).Handle(new GetPayrollTeamQuery(s.RunId, s.Mgr.Id), CancellationToken.None);

        Assert.Empty(team.Lines);
    }

    [Fact]
    public async Task The_team_view_of_a_run_that_does_not_exist_is_not_found()
    {
        var k = new PayrollRunKit();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new GetPayrollTeamQueryHandler(k.Db).Handle(new GetPayrollTeamQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }
}
