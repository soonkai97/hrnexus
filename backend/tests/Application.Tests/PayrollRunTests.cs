using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// The payroll run is an aggregate: a state machine that decides which step may follow which, and who may
/// take it. These tests are the lifecycle table of the Phase 8 plan, one row per test.
///
/// WHY they matter: payroll is the one place where a wrong click moves money. The run must refuse an
/// out-of-order step, must never let the person who prepared the figures approve them (nor a person sign off
/// their own pay), and must freeze what the approver saw so it cannot change underneath the approval.
/// </summary>
public class PayrollRunTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly PayrollRunBasis Basis = new("1", 26m, 8m, 0.5m, 3);

    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly Guid HeadOfHr = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static PayrollRun NewRun(int year = 2026, int month = 10) => PayrollRun.Start(year, month, Preparer, Basis, Now);

    private static PayrollLineResult Result(decimal gross = 3000m, decimal net = 2500m, params PayrollWarning[] warnings) =>
        new(gross, net, gross + 300m, 0m,
            [new PayLine("basic", LineSection.Earning, "Basic salary", gross), new PayLine("epf-employee", LineSection.EmployeeStatutory, "EPF employee", gross - net, Guid.NewGuid())],
            warnings,
            []);

    /// <summary>A draft with Alice and Bob, both calculated.</summary>
    private static PayrollRun DraftWithPeople()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        run.AddLine(PayrollRunLine.Create(Bob, "BOB", "Bob Lim"), Preparer, Now);
        run.ApplyResult(Alice, Result(), Preparer, Now);
        run.ApplyResult(Bob, Result(), Preparer, Now);
        return run;
    }

    private static PayrollRun Submitted()
    {
        var run = DraftWithPeople();
        run.Submit(Preparer, Now);
        return run;
    }

    private static PayrollRun FullySignedOff()
    {
        var run = Submitted();
        run.SignOff(Alice, Manager, Now);
        run.SignOff(Bob, HeadOfHr, Now);
        return run;
    }

    // ── start ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_new_run_is_a_draft_for_that_month_with_the_basis_that_reproduces_its_figures()
    {
        var run = NewRun(2026, 10);

        Assert.Equal(PayrollRunState.Draft, run.State);
        Assert.Equal((2026, 10), (run.Year, run.Month));
        Assert.Equal("1", run.CalculatorVersion);
        Assert.Equal(26m, run.Divisor);
        Assert.Equal(8m, run.HoursPerWorkingDay);
        Assert.Equal(0.5m, run.DeductionCapRate);
        Assert.Equal(3, run.PolicyVersion);
        Assert.Null(run.ExportedAt);
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Started && a.StaffId == Preparer);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 5)]
    [InlineData(2201, 5)]
    public void A_month_that_does_not_exist_is_refused(int year, int month) =>
        Assert.Throws<ArgumentException>(() => NewRun(year, month));

    [Fact]
    public void Each_person_appears_once_in_a_run()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);

        Assert.Throws<InvalidOperationException>(() => run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now));
    }

    // ── editing a draft ───────────────────────────────────────────────────────────

    [Fact]
    public void Every_change_to_a_draft_bumps_the_version_so_a_stale_save_is_refused()
    {
        var run = NewRun();
        var v0 = run.Version;
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        var v1 = run.Version;
        run.SetLineInputs(Alice, 120m, false, null, Preparer, Now);
        var v2 = run.Version;
        run.ApplyResult(Alice, Result(), Preparer, Now);

        Assert.True(v0 < v1 && v1 < v2 && v2 < run.Version);
    }

    [Fact]
    public void The_inputs_of_a_line_are_the_pcb_and_whether_the_person_is_left_out()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);

        run.SetLineInputs(Alice, 120m, true, "On unpaid sabbatical", Preparer, Now);

        var line = run.LineFor(Alice);
        Assert.Equal(120m, line.PcbAmount);
        Assert.True(line.Excluded);
        Assert.Equal("On unpaid sabbatical", line.ExcludedReason);
    }

    [Fact]
    public void Leaving_a_person_out_needs_a_reason_so_nobody_is_silently_skipped()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);

        Assert.Throws<ArgumentException>(() => run.SetLineInputs(Alice, null, true, "  ", Preparer, Now));
    }

    [Fact]
    public void Including_a_person_again_clears_the_reason()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        run.SetLineInputs(Alice, null, true, "Away", Preparer, Now);

        run.SetLineInputs(Alice, null, false, "stale text", Preparer, Now);

        Assert.False(run.LineFor(Alice).Excluded);
        Assert.Null(run.LineFor(Alice).ExcludedReason);
    }

    [Fact]
    public void A_negative_pcb_is_refused()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);

        Assert.Throws<ArgumentException>(() => run.SetLineInputs(Alice, -1m, false, null, Preparer, Now));
    }

    [Fact]
    public void Editing_a_line_that_is_not_in_the_run_is_refused()
    {
        var run = NewRun();

        Assert.Throws<InvalidOperationException>(() => run.SetLineInputs(Alice, 1m, false, null, Preparer, Now));
        Assert.Throws<InvalidOperationException>(() => run.ApplyResult(Alice, Result(), Preparer, Now));
    }

    [Fact]
    public void A_calculation_replaces_the_previous_figures_of_the_line_and_keeps_its_inputs()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        run.SetLineInputs(Alice, 99m, false, null, Preparer, Now);
        run.ApplyResult(Alice, Result(3000m, 2500m), Preparer, Now);

        var unpaid = new PayrollRunUnpaidDayInput(new DateOnly(2026, 10, 5), 1m, Guid.NewGuid(), 115.38m);
        run.ApplyResult(Alice, Result(4000m, 3300m) with { UnpaidDays = [unpaid] }, Preparer, Now);

        var line = run.LineFor(Alice);
        Assert.Equal(4000m, line.Gross);
        Assert.Equal(3300m, line.Net);
        Assert.Equal(2, line.Items.Count);                  // the old lines are gone, not appended to
        Assert.Equal(115.38m, Assert.Single(line.UnpaidDays).DeductionAmount);
        Assert.Equal(99m, line.PcbAmount);                  // the hand-entered input survives recalculation
    }

    [Fact]
    public void A_calculation_stores_each_warning_with_its_code_and_whether_it_blocks()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);

        run.ApplyResult(Alice, Result(warnings: [new PayrollWarning("no-pcb", "No PCB entered.", false), new PayrollWarning("band-gap", "Wage 9999.00 is in no band.", true)]), Preparer, Now);

        var warnings = run.LineFor(Alice).Warnings;
        Assert.Equal(["no-pcb", "band-gap"], warnings.Select(w => w.Code));
        Assert.Equal([false, true], warnings.Select(w => w.Blocking));
    }

    [Fact]
    public void Only_a_draft_can_be_edited()
    {
        var run = Submitted();

        Assert.Throws<InvalidOperationException>(() => run.SetLineInputs(Alice, 1m, false, null, Preparer, Now));
        Assert.Throws<InvalidOperationException>(() => run.ApplyResult(Alice, Result(), Preparer, Now));
        Assert.Throws<InvalidOperationException>(() => run.AddLine(PayrollRunLine.Create(Guid.NewGuid(), "NEW", "New Person"), Preparer, Now));
    }

    [Fact]
    public void Only_a_draft_can_be_deleted()
    {
        NewRun().EnsureCanDelete();
        Assert.Throws<InvalidOperationException>(() => Submitted().EnsureCanDelete());
    }

    // ── submit ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Submitting_freezes_the_run_for_review()
    {
        var run = DraftWithPeople();

        run.Submit(Preparer, Now);

        Assert.Equal(PayrollRunState.Submitted, run.State);
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Submitted && a.StaffId == Preparer);
    }

    [Fact]
    public void A_run_with_nobody_in_it_cannot_be_submitted()
    {
        var run = NewRun();
        Assert.Throws<InvalidOperationException>(() => run.Submit(Preparer, Now));

        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        run.ApplyResult(Alice, Result(), Preparer, Now);
        run.SetLineInputs(Alice, null, true, "Away", Preparer, Now);
        Assert.Throws<InvalidOperationException>(() => run.Submit(Preparer, Now));   // everyone left out
    }

    [Fact]
    public void A_person_who_was_never_calculated_blocks_submit()
    {
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);

        Assert.Throws<InvalidOperationException>(() => run.Submit(Preparer, Now));
    }

    [Fact]
    public void A_blocking_warning_on_a_person_stops_submit()
    {
        var run = DraftWithPeople();
        run.ApplyResult(Bob, Result(warnings: new PayrollWarning("band-gap", "x", true)), Preparer, Now);

        Assert.Throws<InvalidOperationException>(() => run.Submit(Preparer, Now));
    }

    /// <summary>
    /// WHY: every seeded rate row is unverified and nothing can un-verify one, so blocking Submit on it would make
    /// the whole feature unusable. The accountant's gate is Approve (checked against the stored rate row ids).
    /// </summary>
    [Fact]
    public void An_unverified_rate_warning_does_not_stop_submit()
    {
        var run = DraftWithPeople();
        run.ApplyResult(Bob, Result(warnings: new PayrollWarning("unverified-rate", "x", true)), Preparer, Now);

        run.Submit(Preparer, Now);

        Assert.Equal(PayrollRunState.Submitted, run.State);
    }

    [Fact]
    public void A_blocking_warning_on_a_person_who_is_left_out_does_not_stop_submit()
    {
        var run = DraftWithPeople();
        run.ApplyResult(Bob, Result(warnings: new PayrollWarning("band-gap", "x", true)), Preparer, Now);
        run.SetLineInputs(Bob, null, true, "Resigned, final pay handled separately", Preparer, Now);

        run.Submit(Preparer, Now);

        Assert.Equal(PayrollRunState.Submitted, run.State);
    }

    [Fact]
    public void Negative_net_pay_stops_submit()
    {
        var run = DraftWithPeople();
        run.ApplyResult(Bob, Result(1000m, -5m), Preparer, Now);

        Assert.Throws<InvalidOperationException>(() => run.Submit(Preparer, Now));
    }

    [Fact]
    public void Only_a_draft_can_be_submitted()
    {
        Assert.Throws<InvalidOperationException>(() => Submitted().Submit(Preparer, Now));
    }

    // ── sign-off ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Signing_off_a_line_records_who_and_when()
    {
        var run = Submitted();

        run.SignOff(Alice, Manager, Now);

        var line = run.LineFor(Alice);
        Assert.True(line.IsSignedOff);
        Assert.Equal(Manager, line.SignedOffBy);
        Assert.Equal(Now, line.SignedOffAt);
        Assert.False(run.LineFor(Bob).IsSignedOff);
    }

    [Fact]
    public void Signing_off_bumps_the_version()
    {
        var run = Submitted();
        var before = run.Version;

        run.SignOff(Alice, Manager, Now);

        Assert.True(run.Version > before);
    }

    /// <summary>WHY: nobody may confirm their own pay.</summary>
    [Fact]
    public void Nobody_signs_off_their_own_line()
    {
        var run = Submitted();

        Assert.Throws<InvalidOperationException>(() => run.SignOff(Alice, Alice, Now));
    }

    [Theory]
    [InlineData("started")]
    [InlineData("edited")]
    [InlineData("submitted")]
    public void Someone_who_prepared_the_run_cannot_sign_off_a_line(string how)
    {
        var run = NewRun();                                    // Preparer started it
        var editor = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        run.ApplyResult(Alice, Result(), editor, Now);        // editor edited it
        run.Submit(submitter, Now);                           // submitter submitted it
        var signer = how switch { "started" => Preparer, "edited" => editor, _ => submitter };

        Assert.Throws<InvalidOperationException>(() => run.SignOff(Alice, signer, Now));
    }

    [Fact]
    public void A_line_cannot_be_signed_off_twice()
    {
        var run = Submitted();
        run.SignOff(Alice, Manager, Now);

        Assert.Throws<InvalidOperationException>(() => run.SignOff(Alice, HeadOfHr, Now));
    }

    [Fact]
    public void A_person_who_is_left_out_has_no_line_to_sign()
    {
        var run = DraftWithPeople();
        run.SetLineInputs(Bob, null, true, "Away", Preparer, Now);
        run.Submit(Preparer, Now);

        Assert.Throws<InvalidOperationException>(() => run.SignOff(Bob, Manager, Now));
    }

    [Fact]
    public void Only_a_submitted_run_can_be_signed_off()
    {
        Assert.Throws<InvalidOperationException>(() => DraftWithPeople().SignOff(Alice, Manager, Now));
        Assert.Throws<InvalidOperationException>(() => FullySignedOffAndApproved().SignOff(Alice, Manager, Now));
    }

    [Fact]
    public void Signing_a_person_who_is_not_in_the_run_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Submitted().SignOff(Guid.NewGuid(), Manager, Now));
    }

    // ── approve ───────────────────────────────────────────────────────────────────

    private static PayrollRun FullySignedOffAndApproved()
    {
        var run = FullySignedOff();
        run.Approve(HeadOfHr, Now);
        return run;
    }

    [Fact]
    public void A_head_of_hr_approves_once_every_line_is_signed()
    {
        var run = FullySignedOff();

        run.Approve(HeadOfHr, Now);

        Assert.Equal(PayrollRunState.Approved, run.State);
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Approved && a.StaffId == HeadOfHr);
    }

    [Fact]
    public void A_run_with_an_unsigned_line_cannot_be_approved()
    {
        var run = Submitted();
        run.SignOff(Alice, Manager, Now);

        Assert.Throws<InvalidOperationException>(() => run.Approve(HeadOfHr, Now));
    }

    [Fact]
    public void Lines_that_are_left_out_need_no_signature()
    {
        var run = DraftWithPeople();
        run.SetLineInputs(Bob, null, true, "Away", Preparer, Now);
        run.Submit(Preparer, Now);
        run.SignOff(Alice, Manager, Now);

        run.Approve(HeadOfHr, Now);

        Assert.Equal(PayrollRunState.Approved, run.State);
    }

    /// <summary>WHY: separation of duties, no single-person exception (owner decision, 14 Nov 2026).</summary>
    [Theory]
    [InlineData("started")]
    [InlineData("edited")]
    [InlineData("submitted")]
    public void Whoever_prepared_the_run_cannot_approve_it(string how)
    {
        var editor = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        run.ApplyResult(Alice, Result(), editor, Now);
        run.Submit(submitter, Now);
        run.SignOff(Alice, Manager, Now);
        var approver = how switch { "started" => Preparer, "edited" => editor, _ => submitter };

        Assert.Throws<InvalidOperationException>(() => run.Approve(approver, Now));
    }

    [Fact]
    public void Only_a_submitted_run_can_be_approved()
    {
        Assert.Throws<InvalidOperationException>(() => DraftWithPeople().Approve(HeadOfHr, Now));
        Assert.Throws<InvalidOperationException>(() => FullySignedOffAndApproved().Approve(HeadOfHr, Now));
    }

    // ── return ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Returning_a_submitted_run_makes_it_a_draft_again_and_clears_every_sign_off()
    {
        var run = Submitted();
        run.SignOff(Alice, Manager, Now);

        run.Return(HeadOfHr, "PCB for Bob is missing", Now);

        Assert.Equal(PayrollRunState.Draft, run.State);
        Assert.All(run.Lines, l => Assert.False(l.IsSignedOff));
        var returned = Assert.Single(run.Actors, a => a.Action == PayrollRunAction.Returned);
        Assert.Equal("PCB for Bob is missing", returned.Reason);
        Assert.Equal(HeadOfHr, returned.StaffId);
    }

    [Fact]
    public void An_approved_run_can_be_returned_too()
    {
        var run = FullySignedOffAndApproved();

        run.Return(HeadOfHr, "Wrong allowance", Now);

        Assert.Equal(PayrollRunState.Draft, run.State);
        Assert.All(run.Lines, l => Assert.False(l.IsSignedOff));
    }

    [Fact]
    public void Returning_needs_a_reason()
    {
        Assert.Throws<ArgumentException>(() => Submitted().Return(HeadOfHr, " ", Now));
    }

    [Fact]
    public void A_draft_or_a_locked_run_cannot_be_returned()
    {
        Assert.Throws<InvalidOperationException>(() => DraftWithPeople().Return(HeadOfHr, "x", Now));
        Assert.Throws<InvalidOperationException>(() => Locked().Return(HeadOfHr, "x", Now));
    }

    /// <summary>
    /// WHY: after a Return the preparer fixes the figures and submits again. The person who returned it is a
    /// reviewer, and a reviewer may still approve; but anyone who edited it in the new round may not.
    /// </summary>
    [Fact]
    public void After_a_return_the_reviewer_may_approve_the_corrected_run_but_the_new_editor_may_not()
    {
        var run = Submitted();
        run.Return(HeadOfHr, "Fix PCB", Now);
        var fixer = Guid.NewGuid();
        run.SetLineInputs(Alice, 50m, false, null, fixer, Now);
        run.ApplyResult(Alice, Result(), fixer, Now);
        run.Submit(Preparer, Now);
        run.SignOff(Alice, Manager, Now);
        run.SignOff(Bob, Manager, Now);

        Assert.Throws<InvalidOperationException>(() => run.Approve(fixer, Now));
        run.Approve(HeadOfHr, Now);

        Assert.Equal(PayrollRunState.Approved, run.State);
    }

    // ── lock and reopen ───────────────────────────────────────────────────────────

    private static PayrollRun Locked()
    {
        var run = FullySignedOffAndApproved();
        run.Lock(HeadOfHr, Now);
        return run;
    }

    [Fact]
    public void Locking_an_approved_run_records_who_locked_it()
    {
        var run = FullySignedOffAndApproved();

        run.Lock(HeadOfHr, Now);

        Assert.Equal(PayrollRunState.Locked, run.State);
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Locked && a.StaffId == HeadOfHr);
    }

    [Fact]
    public void Only_an_approved_run_can_be_locked()
    {
        Assert.Throws<InvalidOperationException>(() => DraftWithPeople().Lock(HeadOfHr, Now));
        Assert.Throws<InvalidOperationException>(() => Submitted().Lock(HeadOfHr, Now));
        Assert.Throws<InvalidOperationException>(() => Locked().Lock(HeadOfHr, Now));
    }

    [Fact]
    public void Reopening_a_locked_run_makes_it_a_draft_with_no_sign_offs()
    {
        var run = Locked();

        run.Reopen(HeadOfHr, Now);

        Assert.Equal(PayrollRunState.Draft, run.State);
        Assert.All(run.Lines, l => Assert.False(l.IsSignedOff));
        Assert.Contains(run.Actors, a => a.Action == PayrollRunAction.Reopened && a.StaffId == HeadOfHr);
    }

    /// <summary>WHY: once a bank file or payslips have gone out, the figures are history; reopening would rewrite it.</summary>
    [Fact]
    public void A_run_that_was_exported_cannot_be_reopened()
    {
        var run = Locked();
        run.MarkExported(Now);

        Assert.Throws<InvalidOperationException>(() => run.Reopen(HeadOfHr, Now));
    }

    [Fact]
    public void Only_a_locked_run_can_be_reopened_or_exported()
    {
        Assert.Throws<InvalidOperationException>(() => FullySignedOffAndApproved().Reopen(HeadOfHr, Now));
        Assert.Throws<InvalidOperationException>(() => FullySignedOffAndApproved().MarkExported(Now));
    }

    /// <summary>
    /// WHY: a reopened run is prepared again. Whoever reopened it is not thereby a preparer, but whoever edits it
    /// in the new round is, so they cannot also approve it.
    /// </summary>
    [Fact]
    public void After_a_reopen_the_people_who_edit_the_run_cannot_approve_it()
    {
        var run = Locked();
        run.Reopen(HeadOfHr, Now);
        var fixer = Guid.NewGuid();
        run.SetLineInputs(Alice, 10m, false, null, fixer, Now);
        run.ApplyResult(Alice, Result(), fixer, Now);
        run.Submit(Preparer, Now);
        run.SignOff(Alice, Manager, Now);
        run.SignOff(Bob, Manager, Now);

        Assert.Throws<InvalidOperationException>(() => run.Approve(fixer, Now));
        run.Approve(HeadOfHr, Now);
    }


    // ── review findings (opus, 15 Nov 2026) ───────────────────────────────────────

    /// <summary>
    /// WHY: the owner's rule is that whoever prepared the figures never signs or approves them. A Return does not
    /// wash that away, because the hand-entered inputs the person typed are still on the lines.
    /// </summary>
    [Theory]
    [InlineData("signoff")]
    [InlineData("approve")]
    public void Someone_who_edited_in_an_earlier_round_is_still_refused_after_a_return(string step)
    {
        var earlyEditor = Guid.NewGuid();
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), Preparer, Now);
        run.SetLineInputs(Alice, 400m, false, null, earlyEditor, Now);       // typed the PCB in round 0
        run.ApplyResult(Alice, Result(), earlyEditor, Now);
        run.Submit(Preparer, Now);
        run.Return(earlyEditor, "Needs a second look", Now);                // they even returned it themselves
        run.ApplyResult(Alice, Result(), Preparer, Now);
        run.Submit(Preparer, Now);                                           // round 1 is submitted by someone else
        run.SignOff(Alice, Manager, Now);

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (step == "signoff") run.SignOff(Alice, earlyEditor, Now);
            else run.Approve(earlyEditor, Now);
        });
    }

    [Fact]
    public void Whoever_started_the_run_stays_refused_in_every_later_round()
    {
        var starter = Guid.NewGuid();
        var run = PayrollRun.Start(2026, 10, starter, Basis, Now);
        var other = Guid.NewGuid();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), other, Now);
        run.ApplyResult(Alice, Result(), other, Now);
        run.Submit(other, Now);
        run.Return(HeadOfHr, "Redo", Now);
        run.ApplyResult(Alice, Result(), other, Now);
        run.Submit(other, Now);
        run.SignOff(Alice, Manager, Now);

        Assert.Throws<InvalidOperationException>(() => run.Approve(starter, Now));
    }

    [Fact]
    public void Whoever_added_a_person_to_the_run_counts_as_having_prepared_it()
    {
        var adder = Guid.NewGuid();
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(Alice, "ALICE", "Alice Tan"), adder, Now);
        run.ApplyResult(Alice, Result(), Preparer, Now);
        run.Submit(Preparer, Now);
        run.SignOff(Alice, Manager, Now);

        Assert.Throws<InvalidOperationException>(() => run.Approve(adder, Now));
    }

    /// <summary>
    /// WHY: the net pay was worked out with the old PCB. Submitting after a change would put figures in front of
    /// the approver that ignore what the preparer just typed.
    /// </summary>
    [Fact]
    public void Changing_the_pcb_after_a_calculation_means_the_person_must_be_calculated_again_before_submit()
    {
        var run = DraftWithPeople();

        run.SetLineInputs(Alice, 400m, false, null, Preparer, Now);

        Assert.Throws<InvalidOperationException>(() => run.Submit(Preparer, Now));
        run.ApplyResult(Alice, Result(), Preparer, Now);
        run.Submit(Preparer, Now);
        Assert.Equal(PayrollRunState.Submitted, run.State);
    }

    [Fact]
    public void Saving_the_same_inputs_again_does_not_throw_away_a_calculation()
    {
        var run = DraftWithPeople();
        run.SetLineInputs(Alice, 400m, false, null, Preparer, Now);
        run.ApplyResult(Alice, Result(), Preparer, Now);

        run.SetLineInputs(Alice, 400m, false, null, Preparer, Now);

        run.Submit(Preparer, Now);
    }

    [Fact]
    public void Putting_a_left_out_person_back_in_means_they_must_be_calculated_again()
    {
        var run = DraftWithPeople();
        run.SetLineInputs(Bob, null, true, "Away", Preparer, Now);
        run.SetLineInputs(Bob, null, false, null, Preparer, Now);

        Assert.Throws<InvalidOperationException>(() => run.Submit(Preparer, Now));
    }

    [Fact]
    public void Nobody_approves_a_run_that_contains_their_own_pay()
    {
        var approver = Guid.NewGuid();
        var run = NewRun();
        run.AddLine(PayrollRunLine.Create(approver, "HR1", "Head Hr"), Preparer, Now);
        run.ApplyResult(approver, Result(), Preparer, Now);
        run.Submit(Preparer, Now);
        run.SignOff(approver, Manager, Now);

        Assert.Throws<InvalidOperationException>(() => run.Approve(approver, Now));
    }

    [Fact]
    public void An_approver_whose_own_line_is_left_out_may_approve()
    {
        var approver = Guid.NewGuid();
        var run = DraftWithPeople();
        run.AddLine(PayrollRunLine.Create(approver, "HR1", "Head Hr"), Preparer, Now);
        run.SetLineInputs(approver, null, true, "Paid separately", Preparer, Now);
        run.Submit(Preparer, Now);
        run.SignOff(Alice, Manager, Now);
        run.SignOff(Bob, Manager, Now);

        run.Approve(approver, Now);

        Assert.Equal(PayrollRunState.Approved, run.State);
    }

    [Fact]
    public void A_reason_longer_than_the_database_column_is_refused_cleanly()
    {
        var run = DraftWithPeople();
        var tooLong = new string('x', 501);

        Assert.Throws<ArgumentException>(() => run.SetLineInputs(Alice, null, true, tooLong, Preparer, Now));
        run.Submit(Preparer, Now);
        Assert.Throws<ArgumentException>(() => run.Return(HeadOfHr, tooLong, Now));
    }

    /// <summary>WHY: Version is the concurrency token. A step that forgets to bump it can be saved over a stale copy.</summary>
    [Fact]
    public void Every_step_of_the_lifecycle_bumps_the_version()
    {
        var run = DraftWithPeople();
        var versions = new List<long> { run.Version };
        void Step(Action act) { act(); versions.Add(run.Version); }

        Step(() => run.Submit(Preparer, Now));
        Step(() => run.SignOff(Alice, Manager, Now));
        Step(() => run.SignOff(Bob, Manager, Now));
        Step(() => run.Approve(HeadOfHr, Now));
        Step(() => run.Return(HeadOfHr, "again", Now));
        Step(() => run.Submit(Preparer, Now));
        Step(() => run.SignOff(Alice, Manager, Now));
        Step(() => run.SignOff(Bob, Manager, Now));
        Step(() => run.Approve(HeadOfHr, Now));
        Step(() => run.Lock(HeadOfHr, Now));
        Step(() => run.Reopen(HeadOfHr, Now));

        Assert.Equal(versions.Distinct().Count(), versions.Count);          // strictly increasing: no step left it unchanged
        Assert.Equal(versions.OrderBy(v => v), versions);
    }

    // ── totals ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Totals_count_only_people_who_are_in_the_run()
    {
        var run = DraftWithPeople();
        run.ApplyResult(Alice, Result(3000m, 2500m), Preparer, Now);
        run.ApplyResult(Bob, Result(4000m, 3200m), Preparer, Now);
        run.SetLineInputs(Bob, null, true, "Away", Preparer, Now);

        Assert.Equal(1, run.IncludedCount);
        Assert.Equal(3000m, run.TotalGross);
        Assert.Equal(2500m, run.TotalNet);
    }
}
