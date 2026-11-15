using HRNexus.Application.CashAdvance.Commands;
using HRNexus.Application.CashAdvance.Queries;
using HRNexus.Application.Common;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// The monthly repayment of a cash advance, and how it reads back.
///
/// WHY these tests matter: the schedule decides how much of someone's pay payroll takes each month. It must never exceed the
/// advance, must not be changed under a run that is being reviewed (the approver would approve figures that no longer match),
/// and the list must show what has really been taken back, not what was planned.
/// </summary>
public class CashAdvanceRepaymentCommandTests
{
    private static async Task<(PayrollRunKit K, E.Staff Admin, E.Staff Prep, E.Staff Head, E.Staff Alice, E.CashAdvance Advance)> SceneAsync(decimal amount = 500m, decimal basic = 3000m)
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var admin = await k.PersonAsync("CASHADMIN", role: "CASH ADMIN", keys: [AppConstants.Permissions.CashAdvanceManage], salary: false);
        var prep = await k.PreparerAsync();
        var head = await k.HeadOfHrAsync();
        var alice = await k.PersonAsync("ALICE", basic);
        var advance = E.CashAdvance.Create(alice.Id, amount, new DateOnly(2026, 9, 2), "Rent", "admin");
        k.Db.CashAdvances.Add(advance);
        await k.Db.SaveChangesAsync();
        return (k, admin, prep, head, alice, advance);
    }

    private static Task Set(PayrollRunKit k, E.Staff by, Guid advanceId, decimal? perMonth) =>
        new SetCashAdvanceRepaymentCommandHandler(k.Db, k.Audit).Handle(new SetCashAdvanceRepaymentCommand(advanceId, perMonth, by.Id, by.FullName), CancellationToken.None);

    private static async Task<decimal?> Schedule(PayrollRunKit k, Guid id) => (await k.Db.CashAdvances.AsNoTracking().SingleAsync(a => a.Id == id)).RepaymentPerMonth;

    [Fact]
    public async Task The_monthly_repayment_is_set_and_can_be_cleared_without_the_audit_row_carrying_the_amount()
    {
        var (k, admin, _, _, _, advance) = await SceneAsync();

        await Set(k, admin, advance.Id, 123.45m);
        Assert.Equal(123.45m, await Schedule(k, advance.Id));
        await Set(k, admin, advance.Id, null);
        Assert.Null(await Schedule(k, advance.Id));

        Assert.All(k.Audit.Entries.Where(e => e.Type == "CashAdvance"), e => Assert.Null(e.Changes));
        Assert.Equal(2, k.Audit.Entries.Count(e => e.Action == "ScheduleChanged"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(501)]
    public async Task A_monthly_repayment_must_be_more_than_zero_and_no_more_than_the_advance(int perMonth)
    {
        var (k, admin, _, _, _, advance) = await SceneAsync(500m);

        await Assert.ThrowsAsync<ArgumentException>(() => Set(k, admin, advance.Id, perMonth));
    }

    [Fact]
    public async Task A_repayment_with_more_than_two_decimals_is_refused()
    {
        var (k, admin, _, _, _, advance) = await SceneAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Set(k, admin, advance.Id, 10.005m));
    }

    [Fact]
    public async Task A_settled_advance_has_nothing_left_to_schedule()
    {
        var (k, admin, _, _, _, advance) = await SceneAsync();
        (await k.Db.CashAdvances.SingleAsync(a => a.Id == advance.Id)).MarkSettled();
        await k.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Set(k, admin, advance.Id, 100m));
    }

    [Fact]
    public async Task An_advance_that_does_not_exist_is_not_found()
    {
        var (k, admin, _, _, _, _) = await SceneAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Set(k, admin, Guid.NewGuid(), 100m));
    }

    [Fact]
    public async Task Managing_cash_advances_is_checked_inside_the_command()
    {
        var (k, _, prep, _, _, advance) = await SceneAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Set(k, prep, advance.Id, 100m));   // a payroll preparer is not a cash advance manager
        Assert.Null(await Schedule(k, advance.Id));
    }

    // ── interaction with a run ────────────────────────────────────────────────────

    private static async Task<Guid> StartAsync(PayrollRunKit k, E.Staff prep) =>
        await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);

    [Fact]
    public async Task The_schedule_can_change_while_the_run_is_a_draft_and_the_next_calculation_follows_it()
    {
        var (k, admin, prep, _, alice, advance) = await SceneAsync(500m);
        var runId = await StartAsync(k, prep);

        await Set(k, admin, advance.Id, 200m);
        var version = k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new RecalculatePayrollRunCommand(runId, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        var line = (await k.Db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId)).LineFor(alice.Id);
        Assert.Equal(200m, line.Items.Single(i => i.SourceId == advance.Id).Amount);
    }

    /// <summary>WHY: the approver is looking at a figure that includes this advance. Changing the schedule underneath would approve numbers that no longer match.</summary>
    [Fact]
    public async Task The_schedule_cannot_change_while_a_run_that_is_being_reviewed_has_the_advance()
    {
        var (k, admin, prep, _, _, advance) = await SceneAsync(500m);
        var runId = await StartAsync(k, prep);
        var version = k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(runId, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Set(k, admin, advance.Id, 100m));

        Assert.Contains("Return the run", ex.Message);
    }

    [Fact]
    public async Task A_partly_repaid_advance_can_still_be_rescheduled_after_its_run_is_locked()
    {
        var (k, admin, prep, head, _, advance) = await SceneAsync(500m);
        await Set(k, admin, advance.Id, 200m);
        var runId = await StartAsync(k, prep);
        long Version() => k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(runId, Version(), PayrollRunKit.Act(prep)), CancellationToken.None);
        await new SignOffAllPayrollLinesCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SignOffAllPayrollLinesCommand(runId, Version(), PayrollRunKit.Act(head)), CancellationToken.None);
        await new ApprovePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new ApprovePayrollRunCommand(runId, Version(), PayrollRunKit.Act(head)), CancellationToken.None);
        await new LockPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new LockPayrollRunCommand(runId, PayrollRunKit.Act(head)), CancellationToken.None);

        await Set(k, admin, advance.Id, 100m);

        Assert.Equal(100m, await Schedule(k, advance.Id));
    }

    // ── read ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_shows_the_schedule_what_has_been_taken_back_and_what_is_left()
    {
        var (k, admin, _, _, _, advance) = await SceneAsync(500m);
        await Set(k, admin, advance.Id, 200m);
        k.Db.CashAdvanceRepayments.Add(E.CashAdvanceRepayment.Create(advance.Id, Guid.NewGuid(), 200m, DateTime.UtcNow));
        k.Db.CashAdvanceRepayments.Add(E.CashAdvanceRepayment.Create(advance.Id, Guid.NewGuid(), 50.50m, DateTime.UtcNow));
        await k.Db.SaveChangesAsync();

        var row = Assert.Single(await new GetCashAdvancesQueryHandler(k.Db).Handle(new GetCashAdvancesQuery(), CancellationToken.None));

        Assert.Equal((200m, 250.50m, 249.50m), (row.RepaymentPerMonth, row.Repaid, row.Remaining));
    }

    [Fact]
    public async Task An_advance_settled_by_hand_shows_nothing_remaining()
    {
        var (k, _, _, _, _, advance) = await SceneAsync(500m);
        (await k.Db.CashAdvances.SingleAsync(a => a.Id == advance.Id)).MarkSettled();
        await k.Db.SaveChangesAsync();

        var row = Assert.Single(await new GetCashAdvancesQueryHandler(k.Db).Handle(new GetCashAdvancesQuery(), CancellationToken.None));

        Assert.Equal(("settled", 0m), (row.Status, row.Remaining));
    }

    [Fact]
    public async Task The_schedule_cannot_change_while_an_approved_run_has_the_advance_either()
    {
        var (k, admin, prep, head, _, advance) = await SceneAsync(500m);
        var runId = await StartAsync(k, prep);
        long Version() => k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(runId, Version(), PayrollRunKit.Act(prep)), CancellationToken.None);
        await new SignOffAllPayrollLinesCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SignOffAllPayrollLinesCommand(runId, Version(), PayrollRunKit.Act(head)), CancellationToken.None);
        await new ApprovePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new ApprovePayrollRunCommand(runId, Version(), PayrollRunKit.Act(head)), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Set(k, admin, advance.Id, 100m));
    }

    [Fact]
    public async Task An_advance_nothing_was_taken_from_shows_the_whole_amount_as_remaining()
    {
        var (k, _, _, _, _, _) = await SceneAsync(500m);

        var row = Assert.Single(await new GetCashAdvancesQueryHandler(k.Db).Handle(new GetCashAdvancesQuery(), CancellationToken.None));

        Assert.Equal((null, 0m, 500m), (row.RepaymentPerMonth, row.Repaid, row.Remaining));
    }
}
