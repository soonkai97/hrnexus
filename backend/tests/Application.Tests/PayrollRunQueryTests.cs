using HRNexus.Application.Common;
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

    /// <summary>The tracker is cleared before each query so a test cannot pass on entities the commands left loaded: the query must load its own.</summary>
    private static TestDbContext Fresh(Scene s)
    {
        s.K.Db.ChangeTracker.Clear();
        return s.K.Db;
    }

    // ── access is re-checked in the database ──────────────────────────────────────

    /// <summary>WHY: the API decides from a permission cache that can lag a few minutes; pay must stop being readable the moment access is withdrawn.</summary>
    [Fact]
    public async Task Reading_payroll_needs_the_permission_right_now_not_a_cached_answer()
    {
        var s = await DraftAsync();
        var stranger = await s.K.PersonAsync("STRANGER", salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => new GetPayrollRunsQueryHandler(Fresh(s)).Handle(new GetPayrollRunsQuery(stranger.Id), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, stranger.Id), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetPayrollTeamQueryHandler(Fresh(s)).Handle(new GetPayrollTeamQuery(s.RunId, stranger.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Someone_who_has_just_been_deactivated_can_no_longer_read_payroll()
    {
        var s = await DraftAsync();
        (await s.K.Db.Staff.SingleAsync(x => x.Id == s.Head.Id)).Deactivate();
        await s.K.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, s.Head.Id), CancellationToken.None));
    }

    [Fact]
    public async Task The_detail_leaves_out_who_is_not_in_the_run_unless_asked()
    {
        var s = await DraftAsync();
        await s.K.PersonAsync("NOSAL", salary: false);

        var d = await new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, s.Head.Id), CancellationToken.None);

        Assert.Empty(d.NotInRun);
    }

    // ── list ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_totals_count_only_the_people_who_are_in_the_run()
    {
        var s = await DraftAsync();
        await new UpdatePayrollRunLineCommandHandler(s.K.Db, s.K.Audit, s.K.Clock).Handle(
            new UpdatePayrollRunLineCommand(s.RunId, s.Bob.Id, null, true, "Paid separately", s.Version, PayrollRunKit.Act(s.Prep)), CancellationToken.None);

        var run = Assert.Single(await new GetPayrollRunsQueryHandler(Fresh(s)).Handle(new GetPayrollRunsQuery(s.Head.Id), CancellationToken.None));

        Assert.Equal((2026, 10, "draft", 1, 1), (run.Year, run.Month, run.State, run.People, run.LeftOut));
        Assert.Equal(3000m, run.TotalGross);
    }

    [Fact]
    public async Task The_list_is_newest_month_first()
    {
        var s = await DraftAsync();
        s.K.Db.PayrollRuns.Add(E.PayrollRun.Start(2026, 9, s.Prep.Id, new E.PayrollRunBasis("1", 26m, 8m, 0.5m, 0), DateTime.UtcNow));
        await s.K.Db.SaveChangesAsync();

        var runs = await new GetPayrollRunsQueryHandler(Fresh(s)).Handle(new GetPayrollRunsQuery(s.Head.Id), CancellationToken.None);

        Assert.Equal([10, 9], runs.Select(r => r.Month));
    }

    // ── detail ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task One_run_comes_back_with_its_lines_items_history_and_the_rates_it_rests_on()
    {
        var s = await DraftAsync(verified: false);

        var d = await new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, s.Head.Id), CancellationToken.None);

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

        var d = await new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, s.Head.Id), CancellationToken.None);

        Assert.Empty(d.UnverifiedRates);
    }

    [Fact]
    public async Task A_draft_lists_who_cannot_be_in_it_yet_and_who_is_waiting_for_a_recalculation()
    {
        var s = await DraftAsync();
        await s.K.PersonAsync("NOSAL", salary: false);
        await s.K.PersonAsync("LATE");

        var d = await new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, s.Head.Id, IncludeNotInRun: true), CancellationToken.None);

        Assert.Contains(d.NotInRun, n => n.StaffId == "NOSAL" && n.Reason.Contains("salary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(d.NotInRun, n => n.StaffId == "LATE" && n.Reason.Contains("recalculate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_run_past_draft_does_not_work_out_who_is_not_in_it()
    {
        var s = await DraftAsync();
        await s.K.PersonAsync("NOSAL", salary: false);
        await SubmitAsync(s);

        var d = await new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, s.Head.Id, IncludeNotInRun: true), CancellationToken.None);

        Assert.Empty(d.NotInRun);
    }

    [Fact]
    public async Task A_sign_off_shows_who_signed()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);
        await new SignOffPayrollLineCommandHandler(s.K.Db, s.K.Audit, s.K.Clock).Handle(
            new SignOffPayrollLineCommand(s.RunId, s.Alice.Id, s.Version, PayrollRunKit.Act(s.Mgr)), CancellationToken.None);

        var d = await new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, s.Head.Id), CancellationToken.None);

        var line = d.Lines.Single(l => l.StaffId == "ALICE");
        Assert.Equal((s.Mgr.Id, "First MGR"), (line.SignedOffBy, line.SignedOffByName));
        Assert.Equal(1, d.Summary.SignedOff);
    }

    [Fact]
    public async Task A_run_that_does_not_exist_is_not_found()
    {
        var k = new PayrollRunKit();
        var head = await k.HeadOfHrAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new GetPayrollRunQueryHandler(k.Db).Handle(new GetPayrollRunQuery(Guid.NewGuid(), head.Id), CancellationToken.None));
    }

    // ── a manager's team ──────────────────────────────────────────────────────────

    /// <summary>WHY: a manager sees the pay of their direct reports to sign it, and no one else's.</summary>
    [Fact]
    public async Task A_manager_sees_only_the_lines_of_the_people_who_report_to_them()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);

        var mine = await new GetPayrollTeamQueryHandler(Fresh(s)).Handle(new GetPayrollTeamQuery(s.RunId, s.Mgr.Id), CancellationToken.None);
        var theirs = await new GetPayrollTeamQueryHandler(Fresh(s)).Handle(new GetPayrollTeamQuery(s.RunId, s.OtherMgr.Id), CancellationToken.None);

        Assert.Equal("ALICE", Assert.Single(mine.Lines).StaffId);
        Assert.Equal("BOB", Assert.Single(theirs.Lines).StaffId);
    }

    [Fact]
    public async Task Someone_with_no_reports_sees_nothing()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);
        var lonely = await s.K.ManagerAsync("LONELY", salary: false);

        var team = await new GetPayrollTeamQueryHandler(Fresh(s)).Handle(new GetPayrollTeamQuery(s.RunId, lonely.Id), CancellationToken.None);

        Assert.Empty(team.Lines);
    }

    [Fact]
    public async Task A_manager_sees_nothing_of_a_draft_run()
    {
        var s = await DraftAsync();

        var team = await new GetPayrollTeamQueryHandler(Fresh(s)).Handle(new GetPayrollTeamQuery(s.RunId, s.Mgr.Id), CancellationToken.None);

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

        var team = await new GetPayrollTeamQueryHandler(Fresh(s)).Handle(new GetPayrollTeamQuery(s.RunId, s.Mgr.Id), CancellationToken.None);

        Assert.Empty(team.Lines);
    }

    // ── which runs wait for a manager ─────────────────────────────────────────────

    [Fact]
    public async Task A_manager_finds_the_submitted_runs_that_have_their_reports_and_how_many_lines_still_need_their_signature()
    {
        var s = await DraftAsync();
        Assert.Empty(await new GetPayrollTeamRunsQueryHandler(Fresh(s)).Handle(new GetPayrollTeamRunsQuery(s.Mgr.Id), CancellationToken.None));   // a draft is not shown

        await SubmitAsync(s);
        var runs = await new GetPayrollTeamRunsQueryHandler(Fresh(s)).Handle(new GetPayrollTeamRunsQuery(s.Mgr.Id), CancellationToken.None);

        var run = Assert.Single(runs);
        Assert.Equal((s.RunId, 2026, 10, "submitted", 1, 1), (run.RunId, run.Year, run.Month, run.State, run.Lines, run.ToSign));
    }

    [Fact]
    public async Task A_manager_with_no_reports_in_any_run_finds_none_and_a_stranger_is_refused()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);
        var lonely = await s.K.ManagerAsync("LONELY", salary: false);
        var stranger = await s.K.PersonAsync("STRANGER", salary: false);

        Assert.Empty(await new GetPayrollTeamRunsQueryHandler(Fresh(s)).Handle(new GetPayrollTeamRunsQuery(lonely.Id), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetPayrollTeamRunsQueryHandler(Fresh(s)).Handle(new GetPayrollTeamRunsQuery(stranger.Id), CancellationToken.None));
    }

    [Fact]
    public async Task The_team_view_of_a_run_that_does_not_exist_is_not_found()
    {
        var k = new PayrollRunKit();
        var mgr = await k.ManagerAsync("MGR", salary: false);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new GetPayrollTeamQueryHandler(k.Db).Handle(new GetPayrollTeamQuery(Guid.NewGuid(), mgr.Id), CancellationToken.None));
    }

    // ── what the caller may do (the page draws only these buttons) ────────────────

    private static async Task<PayrollRunDetailDto> DetailFor(Scene s, E.Staff who) =>
        await new GetPayrollRunQueryHandler(Fresh(s)).Handle(new GetPayrollRunQuery(s.RunId, who.Id), CancellationToken.None);

    /// <summary>WHY: the page must never work out who may do what; the server says, using the same rules the commands enforce.</summary>
    [Fact]
    public async Task A_draft_offers_the_preparer_the_draft_actions_and_the_head_of_hr_none()
    {
        var s = await DraftAsync();

        Assert.Equal(["edit-lines", "recalculate", "submit", "delete"], (await DetailFor(s, s.Prep)).AllowedActions);
        Assert.Empty((await DetailFor(s, s.Head)).AllowedActions);
    }

    [Fact]
    public async Task A_run_that_was_returned_can_no_longer_be_deleted()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);
        await new ReturnPayrollRunCommandHandler(s.K.Db, s.K.Audit, s.K.Clock).Handle(new ReturnPayrollRunCommand(s.RunId, "PCB missing", s.Version, PayrollRunKit.Act(s.Head)), CancellationToken.None);

        Assert.Equal(["edit-lines", "recalculate", "submit"], (await DetailFor(s, s.Prep)).AllowedActions);
    }

    [Fact]
    public async Task A_submitted_run_lets_the_head_of_hr_sign_approve_and_return_but_not_the_person_who_prepared_it()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);

        var head = await DetailFor(s, s.Head);
        Assert.Equal(["signoff-all", "approve", "return"], head.AllowedActions);
        Assert.All(head.Lines, l => Assert.True(l.CanSignOff));

        var prep = await DetailFor(s, s.Prep);
        Assert.Empty(prep.AllowedActions);
        Assert.All(prep.Lines, l => Assert.False(l.CanSignOff));
    }

    /// <summary>WHY: an approver who is paid in the run cannot approve it (another approver is needed) and cannot sign their own line.</summary>
    [Fact]
    public async Task An_approver_whose_own_pay_is_in_the_run_is_not_offered_approve_or_their_own_line()
    {
        var s = await DraftAsync();
        var k = s.K;
        var paidHead = await k.HeadOfHrAsync("HOHR2", salary: true);   // an approver who is also paid
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new RecalculatePayrollRunCommand(s.RunId, s.Version, PayrollRunKit.Act(s.Prep)), CancellationToken.None);
        await SubmitAsync(s);

        var detail = await DetailFor(s, paidHead);

        Assert.DoesNotContain("approve", detail.AllowedActions);
        Assert.Contains("signoff-all", detail.AllowedActions);
        Assert.False(detail.Lines.Single(l => l.StaffId == "HOHR2").CanSignOff);
        Assert.True(detail.Lines.Single(l => l.StaffId == "ALICE").CanSignOff);
    }

    /// <summary>WHY: a manager signs only their own people; the page must not offer a button the server refuses.</summary>
    [Fact]
    public async Task A_manager_who_can_also_view_payroll_may_sign_only_their_direct_reports()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);
        var viewer = await s.K.PersonAsync("VIEWMGR", role: "VIEWMGR", keys: [HRNexus.Domain.Constants.AppConstants.Permissions.PayrollView, HRNexus.Domain.Constants.AppConstants.Permissions.PayrollSignoff], salary: false);
        (await s.K.Db.Staff.SingleAsync(x => x.Id == s.Alice.Id)).SetReportsTo(viewer.Id);
        await s.K.Db.SaveChangesAsync();

        var detail = await DetailFor(s, viewer);

        Assert.Empty(detail.AllowedActions);
        Assert.Equal(["ALICE"], detail.Lines.Where(l => l.CanSignOff).Select(l => l.StaffId));
    }

    [Fact]
    public async Task The_managers_team_view_marks_which_of_their_lines_they_can_sign()
    {
        var s = await DraftAsync();
        await SubmitAsync(s);

        var team = await new GetPayrollTeamQueryHandler(Fresh(s)).Handle(new GetPayrollTeamQuery(s.RunId, s.Mgr.Id), CancellationToken.None);

        Assert.True(Assert.Single(team.Lines).CanSignOff);
    }

    [Fact]
    public async Task An_approved_run_offers_lock_to_a_locker_and_return_to_the_head_of_hr_and_a_locked_one_offers_reopen()
    {
        var s = await DraftAsync(verified: true);
        await SubmitAsync(s);
        var k = s.K;
        await new SignOffAllPayrollLinesCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SignOffAllPayrollLinesCommand(s.RunId, s.Version, PayrollRunKit.Act(s.Head)), CancellationToken.None);
        await new ApprovePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new ApprovePayrollRunCommand(s.RunId, s.Version, PayrollRunKit.Act(s.Head)), CancellationToken.None);

        Assert.Equal(["lock", "return"], (await DetailFor(s, s.Head)).AllowedActions);

        await new LockPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new LockPayrollRunCommand(s.RunId, s.Version, PayrollRunKit.Act(s.Head)), CancellationToken.None);

        Assert.Equal(["reopen"], (await DetailFor(s, s.Head)).AllowedActions);
        Assert.Empty((await DetailFor(s, s.Prep)).AllowedActions);
    }
}
