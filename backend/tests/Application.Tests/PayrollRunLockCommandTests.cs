using HRNexus.Application.Admin.Commands;
using HRNexus.Application.CashAdvance.Commands;
using HRNexus.Application.Common;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Locking and reopening a run, what lock does to cash advances, and the guards that keep the older manual actions
/// (settle, delete, lock a month, delete a person) from undoing what a run has done.
///
/// WHY these tests matter: a locked run means the month was paid. A cash advance must be marked repaid by exactly
/// what was really taken (a cap can take less than was scheduled), reopening must put every effect back, and nobody
/// may settle, delete or lock around the run by the older, hand-operated routes.
/// </summary>
public class PayrollRunLockCommandTests
{
    private sealed class Ctx
    {
        public required PayrollRunKit Kit { get; init; }
        public required E.Staff Prep { get; init; }
        public required E.Staff Hohr { get; init; }
        public required E.Staff Alice { get; init; }
        public Guid RunId { get; set; }
        public long Version => Kit.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == RunId).Version;
        public PayrollActor P => PayrollRunKit.Act(Prep);
        public PayrollActor H => PayrollRunKit.Act(Hohr);
    }

    /// <summary>ALICE earns <paramref name="basic"/> and owes an advance; the run is approved and ready to lock.</summary>
    private static async Task<(Ctx C, E.CashAdvance? Advance)> ApprovedAsync(decimal basic = 3000m, decimal? advance = null, decimal? perMonth = null,
        int year = 2026, int month = 10, Ctx? existing = null)
    {
        Ctx c;
        if (existing is null)
        {
            var k = new PayrollRunKit();
            await k.SeedAndVerifyRatesAsync(true);
            var prep = await k.PreparerAsync();
            var hohr = await k.HeadOfHrAsync();
            var alice = await k.PersonAsync("ALICE", basic);
            c = new Ctx { Kit = k, Prep = prep, Hohr = hohr, Alice = alice };
        }
        else c = existing;

        E.CashAdvance? adv = null;
        if (advance is { } amount)
        {
            adv = E.CashAdvance.Create(c.Alice.Id, amount, new DateOnly(2026, 9, 2), "Rent", "admin");
            adv.SetRepaymentPerMonth(perMonth);
            c.Kit.Db.CashAdvances.Add(adv);
            await c.Kit.Db.SaveChangesAsync();
        }

        c.RunId = await new StartPayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock).Handle(new StartPayrollRunCommand(year, month, c.P), CancellationToken.None);
        await new SubmitPayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock).Handle(new SubmitPayrollRunCommand(c.RunId, c.Version, c.P), CancellationToken.None);
        await new SignOffAllPayrollLinesCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock).Handle(new SignOffAllPayrollLinesCommand(c.RunId, c.Version, c.H), CancellationToken.None);
        await new ApprovePayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock).Handle(new ApprovePayrollRunCommand(c.RunId, c.Version, c.H), CancellationToken.None);
        return (c, adv);
    }

    private static Task Lock(Ctx c) =>
        new LockPayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock).Handle(new LockPayrollRunCommand(c.RunId, c.H), CancellationToken.None);

    private static Task Reopen(Ctx c) =>
        new ReopenPayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock).Handle(new ReopenPayrollRunCommand(c.RunId, c.H), CancellationToken.None);

    private static async Task<PayrollRun> Load(Ctx c) => await PayrollRunWorkForTests.LoadAsync(c.Kit.Db, c.RunId);

    private static async Task<List<decimal>> Repayments(Ctx c, Guid advanceId) =>
        (await c.Kit.Db.CashAdvanceRepayments.AsNoTracking().Where(r => r.CashAdvanceId == advanceId).ToListAsync()).Select(r => r.Amount).ToList();

    private static async Task<string> StatusOf(Ctx c, Guid advanceId) => (await c.Kit.Db.CashAdvances.AsNoTracking().SingleAsync(a => a.Id == advanceId)).Status;

    // ── lock ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Locking_an_approved_run_locks_it_and_creates_the_months_lock_row()
    {
        var (c, _) = await ApprovedAsync();

        await Lock(c);

        Assert.Equal(PayrollRunState.Locked, (await Load(c)).State);
        var lockRow = await c.Kit.Db.PayrollLocks.SingleAsync();
        Assert.Equal((2026, 10), (lockRow.Year, lockRow.Month));
        Assert.Contains(c.Kit.Audit.Entries, e => e.Action == "Locked" && e.Changes == "2026-10");
    }

    [Fact]
    public async Task Locking_adopts_a_lock_row_that_already_exists_for_the_month()
    {
        var (c, _) = await ApprovedAsync();
        c.Kit.Db.PayrollLocks.Add(PayrollLock.Create(2026, 10, "someone", "older manual lock"));
        await c.Kit.Db.SaveChangesAsync();

        await Lock(c);

        Assert.Single(await c.Kit.Db.PayrollLocks.ToListAsync());
    }

    [Fact]
    public async Task Only_an_approved_run_can_be_locked()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PreparerAsync();
        var hohr = await k.HeadOfHrAsync();
        await k.PersonAsync("ALICE");
        var c = new Ctx { Kit = k, Prep = prep, Hohr = hohr, Alice = prep };
        c.RunId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, c.P), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Lock(c));
        Assert.Empty(await k.Db.PayrollLocks.ToListAsync());
    }

    // ── cash advances ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_advance_deducted_in_full_is_recorded_as_repaid_and_settled()
    {
        var (c, advance) = await ApprovedAsync(3000m, advance: 500m);

        await Lock(c);

        Assert.Equal([500m], await Repayments(c, advance!.Id));
        Assert.Equal(E.CashAdvance.Settled, await StatusOf(c, advance.Id));
    }

    /// <summary>
    /// WHY: with a RM1,000 basic the 50% cap allows RM500, so a RM900 advance is only taken as RM500. The repayment is
    /// what was really taken, and the advance stays outstanding for the rest, never marked settled by the schedule.
    /// </summary>
    [Fact]
    public async Task A_repayment_is_what_was_really_taken_not_what_was_scheduled()
    {
        var (c, advance) = await ApprovedAsync(1000m, advance: 900m);

        await Lock(c);

        Assert.Equal([500m], await Repayments(c, advance!.Id));
        Assert.Equal(E.CashAdvance.Outstanding, await StatusOf(c, advance.Id));
    }

    [Fact]
    public async Task A_monthly_schedule_takes_only_that_amount_and_leaves_the_advance_outstanding()
    {
        var (c, advance) = await ApprovedAsync(3000m, advance: 500m, perMonth: 200m);

        await Lock(c);

        Assert.Equal([200m], await Repayments(c, advance!.Id));
        Assert.Equal(E.CashAdvance.Outstanding, await StatusOf(c, advance.Id));
    }

    [Fact]
    public async Task The_next_month_takes_what_is_still_owed_and_settles_the_advance_exactly_when_it_is_paid_back()
    {
        var (c, advance) = await ApprovedAsync(1000m, advance: 900m);
        await Lock(c);                                   // October took RM500, RM400 is still owed
        c.Kit.Clock = new FixedClock(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));

        await ApprovedAsync(existing: c, year: 2026, month: 11);
        await Lock(c);

        Assert.Equal([400m, 500m], (await Repayments(c, advance!.Id)).Order());
        Assert.Equal(E.CashAdvance.Settled, await StatusOf(c, advance.Id));
    }

    [Fact]
    public async Task A_pay_item_deduction_is_not_mistaken_for_a_cash_advance_when_locking()
    {
        var (c, _) = await ApprovedAsyncWithParkingItemAsync();

        await Lock(c);

        Assert.Empty(await c.Kit.Db.CashAdvanceRepayments.ToListAsync());
    }

    private static async Task<(Ctx, E.CashAdvance?)> ApprovedAsyncWithParkingItemAsync()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PreparerAsync();
        var hohr = await k.HeadOfHrAsync();
        var alice = await k.PersonAsync("ALICE");
        k.Db.PayItems.Add(PayItem.Create(alice.Id, PayItemKind.Deduction, "Parking", 50m, false, false, 1, new DateOnly(2026, 1, 1), null, "admin"));
        await k.Db.SaveChangesAsync();
        return await ApprovedAsync(existing: new Ctx { Kit = k, Prep = prep, Hohr = hohr, Alice = alice });
    }

    // ── reopen ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reopening_puts_back_the_draft_the_lock_row_the_repayments_and_the_advance_status()
    {
        var (c, advance) = await ApprovedAsync(3000m, advance: 500m);
        await Lock(c);

        await Reopen(c);

        Assert.Equal(PayrollRunState.Draft, (await Load(c)).State);
        Assert.Empty(await c.Kit.Db.PayrollLocks.ToListAsync());
        Assert.Empty(await Repayments(c, advance!.Id));
        Assert.Equal(E.CashAdvance.Outstanding, await StatusOf(c, advance.Id));
        Assert.All((await Load(c)).Lines, l => Assert.False(l.IsSignedOff));
        Assert.Contains(c.Kit.Audit.Entries, e => e.Action == "Reopened");
    }

    [Fact]
    public async Task Reopening_a_month_keeps_the_other_months_repayments_and_leaves_the_advance_outstanding_for_the_rest()
    {
        var (c, advance) = await ApprovedAsync(1000m, advance: 900m);
        await Lock(c);                                   // October: 500
        c.Kit.Clock = new FixedClock(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
        await ApprovedAsync(existing: c, year: 2026, month: 11);
        await Lock(c);                                   // November: 400, settled
        var november = c.RunId;

        await Reopen(c);                                 // reopen November only

        Assert.Equal(E.CashAdvance.Outstanding, await StatusOf(c, advance!.Id));
        Assert.Equal([500m], await Repayments(c, advance.Id));
        Assert.NotEqual(Guid.Empty, november);
    }

    /// <summary>WHY: when other repayments alone already cover the advance, reopening this month must not put it back to outstanding.</summary>
    [Fact]
    public async Task Reopening_keeps_an_advance_settled_when_other_repayments_alone_cover_it()
    {
        var (c, advance) = await ApprovedAsync(3000m, advance: 100m);
        await Lock(c);
        c.Kit.Db.CashAdvanceRepayments.Add(E.CashAdvanceRepayment.Create(advance!.Id, Guid.NewGuid(), 100m, DateTime.UtcNow));   // covered elsewhere
        await c.Kit.Db.SaveChangesAsync();

        await Reopen(c);

        Assert.Equal(E.CashAdvance.Settled, await StatusOf(c, advance.Id));
    }

    /// <summary>WHY: once payslips or a bank file have gone out, the figures are history; reopening would rewrite them.</summary>
    [Fact]
    public async Task A_run_that_was_exported_cannot_be_reopened()
    {
        var (c, _) = await ApprovedAsync();
        await Lock(c);
        var run = await c.Kit.Db.PayrollRuns.Include(r => r.Actors).Include(r => r.Lines).SingleAsync();
        run.MarkExported(DateTime.UtcNow);
        await c.Kit.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Reopen(c));
        Assert.Equal(PayrollRunState.Locked, (await Load(c)).State);
    }

    [Fact]
    public async Task An_earlier_month_cannot_be_reopened_once_a_later_months_run_has_moved_past_draft()
    {
        var (c, _) = await ApprovedAsync();
        await Lock(c);
        var october = c.RunId;
        c.Kit.Clock = new FixedClock(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
        await ApprovedAsync(existing: c, year: 2026, month: 11);   // November is approved
        c.RunId = october;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Reopen(c));

        Assert.Contains("later month", ex.Message);
    }

    /// <summary>
    /// WHY: with November still a draft it has already scheduled the same advances. Reopening October would let October
    /// and November each deduct them again, so even a draft later run blocks the reopen.
    /// </summary>
    [Fact]
    public async Task An_earlier_month_cannot_be_reopened_while_a_later_run_exists_even_as_a_draft()
    {
        var (c, _) = await ApprovedAsync();
        await Lock(c);
        var october = c.RunId;
        c.Kit.Clock = new FixedClock(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
        var november = await new StartPayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock).Handle(new StartPayrollRunCommand(2026, 11, c.P), CancellationToken.None);
        c.RunId = october;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Reopen(c));
        Assert.Equal(PayrollRunState.Locked, (await Load(c)).State);
        Assert.NotEqual(Guid.Empty, november);
    }

    [Fact]
    public async Task Locking_and_reopening_need_their_own_permissions_inside_the_command()
    {
        var (c, _) = await ApprovedAsync();
        var clerk = await c.Kit.PersonAsync("CLERK2", role: "CLERK2", keys: [AppConstants.Permissions.PayrollManage], salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => new LockPayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock)
            .Handle(new LockPayrollRunCommand(c.RunId, PayrollRunKit.Act(clerk)), CancellationToken.None));
        await Lock(c);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ReopenPayrollRunCommandHandler(c.Kit.Db, c.Kit.Audit, c.Kit.Clock)
            .Handle(new ReopenPayrollRunCommand(c.RunId, PayrollRunKit.Act(clerk)), CancellationToken.None));
    }

    // ── lock refuses a run whose deductions no longer line up ─────────────────────

    /// <summary>WHY: two runs could each have scheduled the same advance; the second lock would repay more than was lent.</summary>
    [Fact]
    public async Task Locking_refuses_to_repay_a_cash_advance_more_than_was_lent()
    {
        var (c, advance) = await ApprovedAsync(3000m, advance: 500m);
        c.Kit.Db.CashAdvanceRepayments.Add(E.CashAdvanceRepayment.Create(advance!.Id, Guid.NewGuid(), 200m, DateTime.UtcNow));   // another run already took 200
        await c.Kit.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Lock(c));

        Assert.Contains("more than was lent", ex.Message);
        Assert.Equal(PayrollRunState.Approved, (await Load(c)).State);
    }

    [Fact]
    public async Task Locking_refuses_when_a_deducted_advance_was_deleted_after_the_run_was_submitted()
    {
        var (c, advance) = await ApprovedAsync(3000m, advance: 500m);
        c.Kit.Db.CashAdvances.Remove(await c.Kit.Db.CashAdvances.SingleAsync(a => a.Id == advance!.Id));   // straight through the database, past the guard
        await c.Kit.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Lock(c));
        Assert.Empty(await c.Kit.Db.PayrollLocks.ToListAsync());
    }

    [Fact]
    public async Task Locking_refuses_when_a_deducted_advance_was_settled_by_hand_after_the_run_was_submitted()
    {
        var (c, advance) = await ApprovedAsync(3000m, advance: 500m);
        (await c.Kit.Db.CashAdvances.SingleAsync(a => a.Id == advance!.Id)).MarkSettled();
        await c.Kit.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Lock(c));
    }

    // ── guards on the older actions ───────────────────────────────────────────────

    [Fact]
    public async Task A_cash_advance_with_a_repayment_cannot_be_settled_reopened_or_deleted_by_hand()
    {
        var (c, advance) = await ApprovedAsync(1000m, advance: 900m);
        var advanceId = advance!.Id;
        await Lock(c);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SetCashAdvanceSettledCommandHandler(c.Kit.Db).Handle(new SetCashAdvanceSettledCommand(advanceId, true), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeleteCashAdvanceCommandHandler(c.Kit.Db).Handle(new DeleteCashAdvanceCommand(advanceId), CancellationToken.None));
        Assert.True(await c.Kit.Db.CashAdvances.AnyAsync(a => a.Id == advanceId));
    }

    /// <summary>The repayment clause on its own: no run is involved, so only the repayment can be what refuses.</summary>
    [Fact]
    public async Task A_cash_advance_that_has_a_repayment_cannot_be_settled_reopened_or_deleted_even_with_no_run_around()
    {
        var k = new PayrollRunKit();
        var alice = await k.PersonAsync("ALICE");
        var advance = E.CashAdvance.Create(alice.Id, 500m, new DateOnly(2026, 9, 2), null, "admin");
        k.Db.CashAdvances.Add(advance);
        k.Db.CashAdvanceRepayments.Add(E.CashAdvanceRepayment.Create(advance.Id, Guid.NewGuid(), 100m, DateTime.UtcNow));
        await k.Db.SaveChangesAsync();
        var id = advance.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SetCashAdvanceSettledCommandHandler(k.Db).Handle(new SetCashAdvanceSettledCommand(id, true), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SetCashAdvanceSettledCommandHandler(k.Db).Handle(new SetCashAdvanceSettledCommand(id, false), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DeleteCashAdvanceCommandHandler(k.Db).Handle(new DeleteCashAdvanceCommand(id), CancellationToken.None));
    }

    [Fact]
    public async Task An_advance_for_someone_who_was_left_out_of_a_submitted_run_can_still_be_deleted()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        await k.PersonAsync("BOB");
        var advance = E.CashAdvance.Create(alice.Id, 500m, new DateOnly(2026, 9, 2), null, "admin");
        k.Db.CashAdvances.Add(advance);
        await k.Db.SaveChangesAsync();
        var id = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);
        var version = (await PayrollRunWorkForTests.LoadAsync(k.Db, id)).Version;
        version = await new UpdatePayrollRunLineCommandHandler(k.Db, k.Audit, k.Clock).Handle(
            new UpdatePayrollRunLineCommand(id, alice.Id, null, true, "Paid separately", version, PayrollRunKit.Act(prep)), CancellationToken.None);
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        await new DeleteCashAdvanceCommandHandler(k.Db).Handle(new DeleteCashAdvanceCommand(advance.Id), CancellationToken.None);

        Assert.False(await k.Db.CashAdvances.AnyAsync());
    }

    [Fact]
    public async Task A_cash_advance_in_a_submitted_run_cannot_be_deleted_by_hand()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var advance = E.CashAdvance.Create(alice.Id, 500m, new DateOnly(2026, 9, 2), null, "admin");
        k.Db.CashAdvances.Add(advance);
        await k.Db.SaveChangesAsync();
        var id = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);
        var version = (await PayrollRunWorkForTests.LoadAsync(k.Db, id)).Version;
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeleteCashAdvanceCommandHandler(k.Db).Handle(new DeleteCashAdvanceCommand(advance.Id), CancellationToken.None));
    }

    [Fact]
    public async Task A_cash_advance_that_is_only_in_a_draft_run_can_still_be_deleted_and_the_next_calculation_drops_it()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PreparerAsync();
        var alice = await k.PersonAsync("ALICE");
        var advance = E.CashAdvance.Create(alice.Id, 500m, new DateOnly(2026, 9, 2), null, "admin");
        k.Db.CashAdvances.Add(advance);
        await k.Db.SaveChangesAsync();
        var id = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);

        await new DeleteCashAdvanceCommandHandler(k.Db).Handle(new DeleteCashAdvanceCommand(advance.Id), CancellationToken.None);
        var version = (await PayrollRunWorkForTests.LoadAsync(k.Db, id)).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new RecalculatePayrollRunCommand(id, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        Assert.DoesNotContain((await PayrollRunWorkForTests.LoadAsync(k.Db, id)).LineFor(alice.Id).Items, i => i.SourceId == advance.Id);
    }

    [Fact]
    public async Task The_older_lock_and_reopen_actions_refuse_a_month_that_has_a_run()
    {
        var (c, _) = await ApprovedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LockPayrollPeriodCommandHandler(c.Kit.Db, c.Kit.Audit).Handle(new LockPayrollPeriodCommand(2026, 10, "admin"), CancellationToken.None));
        await Lock(c);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ReopenPayrollPeriodCommandHandler(c.Kit.Db, c.Kit.Audit).Handle(new ReopenPayrollPeriodCommand(2026, 10, "admin"), CancellationToken.None));
        Assert.Single(await c.Kit.Db.PayrollLocks.ToListAsync());
    }

    [Fact]
    public async Task The_older_lock_action_still_works_for_a_month_with_no_run()
    {
        var k = new PayrollRunKit();

        var id = await new LockPayrollPeriodCommandHandler(k.Db, k.Audit).Handle(new LockPayrollPeriodCommand(2026, 8, "admin"), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task A_person_who_appears_in_a_payroll_run_cannot_be_deleted()
    {
        var (c, _) = await ApprovedAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeleteStaffCommandHandler(c.Kit.Db).Handle(new DeleteStaffCommand(c.Alice.Id), CancellationToken.None));

        Assert.Contains("Deactivate", ex.Message);
        Assert.True(await c.Kit.Db.Staff.AnyAsync(s => s.Id == c.Alice.Id));
    }

    [Fact]
    public async Task A_person_who_is_in_no_run_can_still_be_deleted()
    {
        var k = new PayrollRunKit();
        var nobody = await k.PersonAsync("NOBODY", salary: false);

        await new DeleteStaffCommandHandler(k.Db).Handle(new DeleteStaffCommand(nobody.Id), CancellationToken.None);

        Assert.False(await k.Db.Staff.AnyAsync(s => s.Id == nobody.Id));
    }
}
