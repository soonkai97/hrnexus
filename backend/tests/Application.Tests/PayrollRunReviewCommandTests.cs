using HRNexus.Application.Common;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Submitting, signing off, approving and returning a payroll run.
///
/// WHY these tests matter: these steps are where one person's pay is confirmed by another. A manager must only
/// confirm their own direct reports, the head of HR may confirm anyone, nobody may confirm their own pay or figures
/// they prepared, and the accountant's rate check must stop an approval, because an approved run is the one thing
/// that becomes real money.
/// </summary>
public class PayrollRunReviewCommandTests
{
    /// <summary>October 2026: ALICE (reports to MGR) and MGR are in the run; HOHR (head of HR) and PREP (preparer) are not paid through it.</summary>
    private sealed class Scene
    {
        public required PayrollRunKit Kit { get; init; }
        public required E.Staff Prep { get; init; }
        public required E.Staff Hohr { get; init; }
        public required E.Staff Mgr { get; init; }
        public required E.Staff Alice { get; init; }
        public Guid RunId { get; set; }
        public long Version => Kit.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == RunId).Version;
        public PayrollActor P => PayrollRunKit.Act(Prep);
        public PayrollActor H => PayrollRunKit.Act(Hohr);
        public PayrollActor M => PayrollRunKit.Act(Mgr);
    }

    private static async Task<Scene> DraftAsync(bool verifiedRates = false)
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(verifiedRates);
        var prep = await k.PreparerAsync();
        var hohr = await k.HeadOfHrAsync();
        var mgr = await k.ManagerAsync("MGR");
        var alice = await k.PersonAsync("ALICE", reportsTo: mgr.Id);
        var scene = new Scene { Kit = k, Prep = prep, Hohr = hohr, Mgr = mgr, Alice = alice };
        scene.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new StartPayrollRunCommand(2026, 10, scene.P), CancellationToken.None);
        return scene;
    }

    private static async Task<Scene> SubmittedAsync(bool verifiedRates = false)
    {
        var s = await DraftAsync(verifiedRates);
        await Submit(s);
        return s;
    }

    private static Task<long> Submit(Scene s, long? version = null) =>
        new SubmitPayrollRunCommandHandler(s.Kit.Db, s.Kit.Audit, s.Kit.Clock).Handle(new SubmitPayrollRunCommand(s.RunId, version ?? s.Version, s.P), CancellationToken.None);

    private static Task<long> SignOff(Scene s, E.Staff by, E.Staff person) =>
        new SignOffPayrollLineCommandHandler(s.Kit.Db, s.Kit.Audit, s.Kit.Clock).Handle(new SignOffPayrollLineCommand(s.RunId, person.Id, PayrollRunKit.Act(by)), CancellationToken.None);

    private static Task<int> SignOffAll(Scene s, E.Staff by) =>
        new SignOffAllPayrollLinesCommandHandler(s.Kit.Db, s.Kit.Audit, s.Kit.Clock).Handle(new SignOffAllPayrollLinesCommand(s.RunId, PayrollRunKit.Act(by)), CancellationToken.None);

    private static Task<long> Approve(Scene s, E.Staff by, long? version = null) =>
        new ApprovePayrollRunCommandHandler(s.Kit.Db, s.Kit.Audit, s.Kit.Clock).Handle(new ApprovePayrollRunCommand(s.RunId, version ?? s.Version, PayrollRunKit.Act(by)), CancellationToken.None);

    private static Task<long> Return(Scene s, E.Staff by, string reason = "PCB is missing") =>
        new ReturnPayrollRunCommandHandler(s.Kit.Db, s.Kit.Audit, s.Kit.Clock).Handle(new ReturnPayrollRunCommand(s.RunId, reason, s.Version, PayrollRunKit.Act(by)), CancellationToken.None);

    private static async Task<PayrollRun> Load(Scene s) => await PayrollRunWorkForTests.LoadAsync(s.Kit.Db, s.RunId);

    // ── submit ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submitting_freezes_the_run_for_review_and_records_who_submitted()
    {
        var s = await DraftAsync();
        var before = s.Version;

        var version = await Submit(s);

        var run = await Load(s);
        Assert.Equal(PayrollRunState.Submitted, run.State);
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Submitted && a.StaffId == s.Prep.Id);
        Assert.True(version > before);
        Assert.Contains(s.Kit.Audit.Entries, e => e.Action == "Submitted" && e.Changes == "2026-10");
    }

    /// <summary>WHY: leave approved after the draft was last calculated must be in what the approver sees.</summary>
    [Fact]
    public async Task Submitting_calculates_everyone_once_more_so_the_approver_sees_the_figures_of_that_moment()
    {
        var s = await DraftAsync();
        var leave = LeaveRequest.Create(s.Alice.Id, "unpaid", new DateTime(2026, 10, 7), new DateTime(2026, 10, 7), null, null, 8m, null);
        leave.Update("unpaid", new DateTime(2026, 10, 7), new DateTime(2026, 10, 7), null, null, 8m, null, "approved");
        s.Kit.Db.LeaveRequests.Add(leave);
        await s.Kit.Db.SaveChangesAsync();

        await Submit(s);

        Assert.Equal(3000m - 115.38m, (await Load(s)).LineFor(s.Alice.Id).Gross);
    }

    [Fact]
    public async Task A_submit_from_a_stale_copy_is_refused_and_the_run_stays_a_draft()
    {
        var s = await DraftAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(s, version: -1));

        Assert.Equal(PayrollRunState.Draft, (await Load(s)).State);
    }

    /// <summary>WHY: a person whose date of birth is missing was calculated at an assumed age; that cannot go to an approver.</summary>
    [Fact]
    public async Task A_blocking_warning_stops_the_submit_with_a_message_that_has_no_figures()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        await k.PersonAsync("NODOB", 4321m, dob: false);
        var scene = new Scene { Kit = k, Prep = prep, Hohr = prep, Mgr = prep, Alice = prep };
        scene.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, scene.P), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(scene));

        Assert.DoesNotContain("4321", ex.Message);
        Assert.Equal(PayrollRunState.Draft, (await Load(scene)).State);
    }

    [Fact]
    public async Task Someone_who_became_eligible_since_the_last_calculation_must_be_added_before_submit()
    {
        var s = await DraftAsync();
        await s.Kit.PersonAsync("LATE");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(s));

        Assert.Contains("Recalculate", ex.Message);
    }

    // ── sign-off ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_manager_signs_off_the_line_of_someone_who_reports_to_them()
    {
        var s = await SubmittedAsync();

        await SignOff(s, s.Mgr, s.Alice);

        var line = (await Load(s)).LineFor(s.Alice.Id);
        Assert.Equal(s.Mgr.Id, line.SignedOffBy);
        Assert.Contains(s.Kit.Audit.Entries, e => e.Action == "SignedOff" && e.Changes == "2026-10 ALICE");
    }

    [Fact]
    public async Task A_manager_cannot_sign_off_someone_who_does_not_report_to_them()
    {
        var s = await SubmittedAsync();
        var other = await s.Kit.ManagerAsync("OTHERMGR", salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => SignOff(s, other, s.Alice));
        Assert.False((await Load(s)).LineFor(s.Alice.Id).IsSignedOff);
    }

    /// <summary>WHY: a department head signs their direct reports (managers), not everyone below them.</summary>
    [Fact]
    public async Task A_manager_of_a_manager_cannot_sign_off_the_managers_reports()
    {
        var s = await DraftAsync();
        var hod = await s.Kit.ManagerAsync("HOD", salary: false);
        (await s.Kit.Db.Staff.SingleAsync(x => x.Id == s.Mgr.Id)).SetReportsTo(hod.Id);
        await s.Kit.Db.SaveChangesAsync();
        await Submit(s);

        await Assert.ThrowsAsync<ForbiddenException>(() => SignOff(s, hod, s.Alice));   // Alice reports to MGR, not to HOD
        await SignOff(s, hod, s.Mgr);                                                    // MGR reports to HOD
    }

    [Fact]
    public async Task A_manager_without_the_signoff_permission_cannot_sign_even_their_own_reports()
    {
        var s = await DraftAsync();
        var lead = await s.Kit.PersonAsync("LEAD", role: "TEAM LEAD", keys: [AppConstants.Permissions.StaffView], salary: false);
        var reporter = await s.Kit.PersonAsync("REPORTER", reportsTo: lead.Id);
        await new RecalculatePayrollRunCommandHandler(s.Kit.Db, s.Kit.Audit, s.Kit.Clock)
            .Handle(new RecalculatePayrollRunCommand(s.RunId, s.Version, s.P), CancellationToken.None);   // adds REPORTER to the run
        await Submit(s);

        await Assert.ThrowsAsync<ForbiddenException>(() => SignOff(s, lead, reporter));
    }

    [Fact]
    public async Task A_person_with_no_payroll_permission_cannot_sign_off_anyone()
    {
        var s = await SubmittedAsync();
        var stranger = await s.Kit.PersonAsync("STRANGER", salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => SignOff(s, stranger, s.Alice));
    }

    [Fact]
    public async Task A_deactivated_manager_cannot_sign_off()
    {
        var s = await SubmittedAsync();
        var mgr = await s.Kit.Db.Staff.SingleAsync(x => x.Id == s.Mgr.Id);
        mgr.Deactivate();
        await s.Kit.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => SignOff(s, s.Mgr, s.Alice));
    }

    [Fact]
    public async Task The_head_of_hr_signs_off_anyones_line()
    {
        var s = await SubmittedAsync();

        await SignOff(s, s.Hohr, s.Alice);
        await SignOff(s, s.Hohr, s.Mgr);

        Assert.All((await Load(s)).Lines, l => Assert.Equal(s.Hohr.Id, l.SignedOffBy));
    }

    [Fact]
    public async Task Nobody_signs_off_their_own_line_not_even_the_head_of_hr()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var hohr = await k.HeadOfHrAsync(salary: true);
        var s = new Scene { Kit = k, Prep = prep, Hohr = hohr, Mgr = hohr, Alice = hohr };
        s.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, s.P), CancellationToken.None);
        await Submit(s);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SignOff(s, hohr, hohr));
    }

    /// <summary>WHY: even a head of HR who also prepared the run may not confirm their own figures.</summary>
    [Fact]
    public async Task Someone_who_prepared_the_run_cannot_sign_off_even_with_the_approve_permission()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PersonAsync("BOTH", role: "PREP AND APPROVE", keys: [AppConstants.Permissions.PayrollManage, AppConstants.Permissions.PayrollApprove], salary: false);
        var alice = await k.PersonAsync("ALICE");
        var s = new Scene { Kit = k, Prep = prep, Hohr = prep, Mgr = prep, Alice = alice };
        s.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, s.P), CancellationToken.None);
        await Submit(s);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SignOff(s, prep, alice));
    }

    [Fact]
    public async Task Signing_off_all_covers_every_waiting_line_except_the_signers_own()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(false);
        var prep = await k.PreparerAsync();
        var hohr = await k.HeadOfHrAsync(salary: true);
        var alice = await k.PersonAsync("ALICE");
        var s = new Scene { Kit = k, Prep = prep, Hohr = hohr, Mgr = hohr, Alice = alice };
        s.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, s.P), CancellationToken.None);
        await Submit(s);

        var signed = await SignOffAll(s, hohr);

        var run = await Load(s);
        Assert.Equal(1, signed);
        Assert.True(run.LineFor(alice.Id).IsSignedOff);
        Assert.False(run.LineFor(hohr.Id).IsSignedOff);
        Assert.Equal(0, await SignOffAll(s, hohr));
    }

    [Fact]
    public async Task Only_the_head_of_hr_can_sign_off_all()
    {
        var s = await SubmittedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => SignOffAll(s, s.Mgr));
    }

    // ── approve ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_run_cannot_be_approved_while_a_line_is_unsigned()
    {
        var s = await SubmittedAsync(verifiedRates: true);
        await SignOff(s, s.Mgr, s.Alice);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Approve(s, s.Hohr));
    }

    /// <summary>
    /// WHY: every seeded rate is unverified until an accountant checks it. An approved run is the one that becomes real
    /// pay, so it cannot rest on rates nobody has confirmed. The message names the scheme and class, never a figure.
    /// </summary>
    [Fact]
    public async Task A_run_cannot_be_approved_while_a_rate_it_used_is_unverified_and_the_message_names_no_figures()
    {
        var s = await SubmittedAsync(verifiedRates: false);
        await SignOffAll(s, s.Hohr);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Approve(s, s.Hohr));

        Assert.Contains("EPF (citizen)", ex.Message);
        Assert.DoesNotContain("3000", ex.Message);
        Assert.Equal(PayrollRunState.Submitted, (await Load(s)).State);
    }

    [Fact]
    public async Task Once_the_accountant_has_verified_the_rates_the_head_of_hr_approves()
    {
        var s = await SubmittedAsync(verifiedRates: false);
        await SignOffAll(s, s.Hohr);
        await s.Kit.VerifyAllRatesAsync();

        await Approve(s, s.Hohr);

        var run = await Load(s);
        Assert.Equal(PayrollRunState.Approved, run.State);
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Approved && a.StaffId == s.Hohr.Id);
        Assert.Contains(s.Kit.Audit.Entries, e => e.Action == "Approved");
    }

    [Fact]
    public async Task A_manager_cannot_approve_a_run_even_after_signing_their_lines()
    {
        var s = await SubmittedAsync(verifiedRates: true);
        await SignOffAll(s, s.Hohr);

        await Assert.ThrowsAsync<ForbiddenException>(() => Approve(s, s.Mgr));
    }

    [Fact]
    public async Task The_preparer_cannot_approve_the_run_they_prepared()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PersonAsync("BOTH", role: "PREP AND APPROVE", keys: [AppConstants.Permissions.PayrollManage, AppConstants.Permissions.PayrollApprove], salary: false);
        var hohr = await k.HeadOfHrAsync();
        var alice = await k.PersonAsync("ALICE");
        var s = new Scene { Kit = k, Prep = prep, Hohr = hohr, Mgr = hohr, Alice = alice };
        s.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, s.P), CancellationToken.None);
        await Submit(s);
        await SignOffAll(s, hohr);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Approve(s, prep));
        await Approve(s, hohr);
    }

    [Fact]
    public async Task An_approval_from_a_stale_copy_is_refused()
    {
        var s = await SubmittedAsync(verifiedRates: true);
        await SignOffAll(s, s.Hohr);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Approve(s, s.Hohr, version: -1));
    }

    // ── return ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_head_of_hr_returns_a_submitted_run_with_a_reason_and_every_sign_off_is_cleared()
    {
        var s = await SubmittedAsync();
        await SignOff(s, s.Mgr, s.Alice);

        await Return(s, s.Hohr, "PCB is missing for Alice");

        var run = await Load(s);
        Assert.Equal(PayrollRunState.Draft, run.State);
        Assert.All(run.Lines, l => Assert.False(l.IsSignedOff));
        Assert.Equal("PCB is missing for Alice", run.Actors.Single(a => a.Action == PayrollRunAction.Returned).Reason);
        var audit = s.Kit.Audit.Entries.Single(e => e.Action == "Returned");
        Assert.DoesNotContain("PCB", audit.Changes);   // the reason lives in the run's history, not the audit row
    }

    [Fact]
    public async Task Only_the_head_of_hr_can_return_a_run()
    {
        var s = await SubmittedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Return(s, s.Mgr));
    }

    [Fact]
    public async Task Returning_needs_a_reason()
    {
        var s = await SubmittedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Return(s, s.Hohr, " "));
    }
}
