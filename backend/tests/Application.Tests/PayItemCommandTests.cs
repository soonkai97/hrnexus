using HRNexus.Application.Common;
using HRNexus.Application.Payroll.Commands;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Application.Payroll.Queries;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// A person's recurring allowances and deductions.
///
/// WHY these tests matter: a pay item changes pay every month until it ends. A negative or absurd amount, an item that
/// can be deleted after a payslip already paid it, or a record that tells the audit trail what someone earns would each
/// do quiet damage. The history of a locked month must stay exactly as it was paid.
/// </summary>
public class PayItemCommandTests
{
    private static readonly DateOnly From = new(2026, 1, 1);

    private static Task<Guid> Add(PayrollRunKit k, E.Staff by, Guid employee, string kind = E.PayItemKind.Allowance, decimal amount = 150m,
        string reason = "Transport", DateOnly? from = null, DateOnly? to = null, int priority = 0) =>
        new AddPayItemCommandHandler(k.Db, k.Audit).Handle(
            new AddPayItemCommand(employee, kind, reason, amount, true, true, priority, from ?? From, to, PayrollRunKit.Act(by)), CancellationToken.None);

    private static Task End(PayrollRunKit k, E.Staff by, Guid employee, Guid item, DateOnly to) =>
        new EndPayItemCommandHandler(k.Db, k.Audit).Handle(new EndPayItemCommand(employee, item, to, PayrollRunKit.Act(by)), CancellationToken.None);

    private static Task Delete(PayrollRunKit k, E.Staff by, Guid employee, Guid item) =>
        new DeletePayItemCommandHandler(k.Db, k.Audit).Handle(new DeletePayItemCommand(employee, item, PayrollRunKit.Act(by)), CancellationToken.None);

    private static async Task<(PayrollRunKit K, E.Staff Prep, E.Staff Head, E.Staff Alice)> SceneAsync()
    {
        var k = new PayrollRunKit();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PreparerAsync();
        var head = await k.HeadOfHrAsync();
        var alice = await k.PersonAsync("ALICE", 3000m);
        return (k, prep, head, alice);
    }

    /// <summary>Starts October, submits it and (optionally) locks it so a run past draft has used the item.</summary>
    private static async Task<Guid> RunPastDraftAsync(PayrollRunKit k, E.Staff prep, E.Staff head, bool lockIt)
    {
        var id = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);
        long Version() => k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == id).Version;
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(id, Version(), PayrollRunKit.Act(prep)), CancellationToken.None);
        if (!lockIt) return id;
        await new SignOffAllPayrollLinesCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SignOffAllPayrollLinesCommand(id, Version(), PayrollRunKit.Act(head)), CancellationToken.None);
        await new ApprovePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new ApprovePayrollRunCommand(id, Version(), PayrollRunKit.Act(head)), CancellationToken.None);
        await new LockPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new LockPayrollRunCommand(id, PayrollRunKit.Act(head)), CancellationToken.None);
        return id;
    }

    // ── add ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_allowance_is_added_with_everything_it_was_given()
    {
        var (k, prep, _, alice) = await SceneAsync();

        var id = await Add(k, prep, alice.Id, amount: 150.75m, to: new DateOnly(2026, 12, 31));

        var item = await k.Db.PayItems.AsNoTracking().SingleAsync(i => i.Id == id);
        Assert.Equal((alice.Id, "allowance", "Transport", 150.75m, From, (DateOnly?)new DateOnly(2026, 12, 31)), (item.EmployeeId, item.Kind, item.Reason, item.Amount, item.From, item.To));
        Assert.Equal("First PREP", item.CreatedBy);
    }

    /// <summary>WHY: the audit trail says THAT an item was added and from when, never what it pays.</summary>
    [Fact]
    public async Task Adding_an_item_is_audited_without_the_amount_or_the_reason()
    {
        var (k, prep, _, alice) = await SceneAsync();

        await Add(k, prep, alice.Id, amount: 987.65m, reason: "Secret bonus");

        var row = Assert.Single(k.Audit.Entries, e => e.Type == "PayItem");
        Assert.Equal(("Created", "allowance from 2026-01-01"), (row.Action, row.Changes));
        Assert.DoesNotContain("987", row.Changes);
        Assert.DoesNotContain("Secret", row.Changes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000001)]
    public async Task An_amount_that_is_not_positive_or_is_absurd_is_refused(int amount)
    {
        var (k, prep, _, alice) = await SceneAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Add(k, prep, alice.Id, amount: amount));
    }

    [Fact]
    public async Task An_amount_with_more_than_two_decimals_is_refused()
    {
        var (k, prep, _, alice) = await SceneAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Add(k, prep, alice.Id, amount: 10.005m));
    }

    [Fact]
    public async Task A_deduction_priority_outside_the_range_is_refused_so_nothing_can_outrank_the_cash_advances()
    {
        var (k, prep, _, alice) = await SceneAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Add(k, prep, alice.Id, E.PayItemKind.Deduction, priority: int.MinValue));
    }

    [Fact]
    public async Task An_item_for_someone_who_does_not_exist_is_not_found()
    {
        var (k, prep, _, _) = await SceneAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Add(k, prep, Guid.NewGuid()));
    }

    [Fact]
    public async Task Changing_pay_needs_the_prepare_permission_inside_the_command()
    {
        var (k, _, head, alice) = await SceneAsync();
        var stranger = await k.PersonAsync("STRANGER", salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => Add(k, stranger, alice.Id));
        Assert.False(await k.Db.PayItems.AnyAsync());
        Assert.NotNull(head);
    }

    // ── the next run picks it up ──────────────────────────────────────────────────

    [Fact]
    public async Task A_new_allowance_shows_up_on_the_next_calculation_of_a_draft_run()
    {
        var (k, prep, _, alice) = await SceneAsync();
        var runId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);
        await Add(k, prep, alice.Id, amount: 150m);

        var version = k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new RecalculatePayrollRunCommand(runId, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        var line = (await k.Db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId)).LineFor(alice.Id);
        Assert.Equal(3150m, line.Gross);
        Assert.Contains(line.Items, i => i.Type == "allowance" && i.Amount == 150m);
    }

    // ── end ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_item_can_be_given_an_end_date_and_stops_applying_after_it()
    {
        var (k, prep, _, alice) = await SceneAsync();
        var id = await Add(k, prep, alice.Id);

        await End(k, prep, alice.Id, id, new DateOnly(2026, 9, 30));

        var item = await k.Db.PayItems.AsNoTracking().SingleAsync(i => i.Id == id);
        Assert.Equal(new DateOnly(2026, 9, 30), item.To);
        Assert.False(item.AppliesInMonth(2026, 10));
        Assert.Contains(k.Audit.Entries, e => e.Action == "Ended" && e.Changes == "allowance ends 2026-09-30");
    }

    [Fact]
    public async Task An_item_cannot_end_before_it_starts()
    {
        var (k, prep, _, alice) = await SceneAsync();
        var id = await Add(k, prep, alice.Id, from: new DateOnly(2026, 6, 1));

        await Assert.ThrowsAsync<ArgumentException>(() => End(k, prep, alice.Id, id, new DateOnly(2026, 5, 31)));
    }

    /// <summary>WHY: October's payslips already paid this item. Ending it as of September would rewrite what was paid.</summary>
    [Fact]
    public async Task An_item_cannot_end_before_the_last_month_a_submitted_or_locked_run_paid_it()
    {
        var (k, prep, head, alice) = await SceneAsync();
        var id = await Add(k, prep, alice.Id);
        await RunPastDraftAsync(k, prep, head, lockIt: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => End(k, prep, alice.Id, id, new DateOnly(2026, 9, 30)));
        Assert.Contains("later month", ex.Message);

        await End(k, prep, alice.Id, id, new DateOnly(2026, 10, 31));   // ending in the month it was last paid is fine
    }

    [Fact]
    public async Task Ending_an_item_that_belongs_to_someone_else_is_not_found()
    {
        var (k, prep, _, alice) = await SceneAsync();
        var bob = await k.PersonAsync("BOB");
        var id = await Add(k, prep, alice.Id);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => End(k, prep, bob.Id, id, new DateOnly(2026, 12, 31)));
    }

    // ── delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_item_entered_by_mistake_can_be_deleted_and_the_next_calculation_drops_it()
    {
        var (k, prep, _, alice) = await SceneAsync();
        var runId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);
        var id = await Add(k, prep, alice.Id, amount: 150m);
        var version = k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new RecalculatePayrollRunCommand(runId, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        await Delete(k, prep, alice.Id, id);   // only a draft run has used it
        version = k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new RecalculatePayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new RecalculatePayrollRunCommand(runId, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        Assert.False(await k.Db.PayItems.AnyAsync());
        Assert.Equal(3000m, (await k.Db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId)).LineFor(alice.Id).Gross);
    }

    /// <summary>WHY: once a submitted or locked run paid the item, deleting it would leave payslip lines that name nothing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_item_that_a_submitted_or_locked_run_used_cannot_be_deleted(bool locked)
    {
        var (k, prep, head, alice) = await SceneAsync();
        var id = await Add(k, prep, alice.Id);
        await RunPastDraftAsync(k, prep, head, lockIt: locked);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Delete(k, prep, alice.Id, id));

        Assert.Contains("End it instead", ex.Message);
        Assert.True(await k.Db.PayItems.AnyAsync(i => i.Id == id));
    }

    [Fact]
    public async Task An_item_for_a_person_who_was_left_out_of_the_run_can_still_be_deleted()
    {
        var (k, prep, _, alice) = await SceneAsync();
        var id = await Add(k, prep, alice.Id);
        await k.PersonAsync("BOB");   // someone else stays in the run
        var runId = await new StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);
        var version = k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        version = await new UpdatePayrollRunLineCommandHandler(k.Db, k.Audit, k.Clock).Handle(
            new UpdatePayrollRunLineCommand(runId, alice.Id, null, true, "Paid separately", version, PayrollRunKit.Act(prep)), CancellationToken.None);
        await new SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock).Handle(new SubmitPayrollRunCommand(runId, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        await Delete(k, prep, alice.Id, id);

        Assert.False(await k.Db.PayItems.AnyAsync());
    }

    // ── read ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_shows_each_item_with_its_amount_and_whether_a_run_has_used_it()
    {
        var (k, prep, head, alice) = await SceneAsync();
        var used = await Add(k, prep, alice.Id, amount: 150m, reason: "Transport");
        await RunPastDraftAsync(k, prep, head, lockIt: false);
        var fresh = await Add(k, prep, alice.Id, E.PayItemKind.Deduction, 50m, "Parking", priority: 2, from: new DateOnly(2026, 11, 1));

        var items = await new GetPayItemsQueryHandler(k.Db).Handle(new GetPayItemsQuery(alice.Id, head.Id), CancellationToken.None);

        Assert.Equal(["Parking", "Transport"], items.Select(i => i.Reason));   // newest start first
        Assert.True(items.Single(i => i.Id == used).InUse);
        Assert.False(items.Single(i => i.Id == fresh).InUse);
        Assert.Equal((50m, 2), (items[0].Amount, items[0].Priority));
    }

    [Fact]
    public async Task Reading_pay_items_needs_payroll_view_right_now()
    {
        var (k, prep, head, alice) = await SceneAsync();
        await Add(k, prep, alice.Id);
        var stranger = await k.PersonAsync("STRANGER", salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => new GetPayItemsQueryHandler(k.Db).Handle(new GetPayItemsQuery(alice.Id, stranger.Id), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new GetPayItemsQueryHandler(k.Db).Handle(new GetPayItemsQuery(Guid.NewGuid(), head.Id), CancellationToken.None));
    }
}
