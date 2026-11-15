using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using HRNexus.Infrastructure.Auth;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// The payroll run commands against a real PostgreSQL: the Serializable transaction, the version token, the unique
/// month index, encrypted amounts and the whole lifecycle in one pass.
///
/// WHY these tests matter: InMemory cannot show that two people starting the same month, or editing one run at once,
/// end with exactly one winner and a clear message for the other; nor that a repayment written at lock time is
/// encrypted and read back exactly. Each test uses its own throwaway database.
/// </summary>
[Collection("postgres")]
public class PayrollRunCommandsPostgresTests(PostgresFixture fx)
{
    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => UtcNow;
    }

    private sealed class Audit : IAuditService
    {
        public List<string> Actions { get; } = [];
        public Task LogAsync(string entityType, string entityId, string action, Guid? performedById, string performedByName, string? changes = null, CancellationToken ct = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed record People(Guid Prep, Guid Hohr, Guid Alice, string Database);

    /// <summary>A database with the seeded (unverified) rates, a preparer, a head of HR and one paid person.</summary>
    private async Task<People> SeedAsync(bool verifyRates = true, decimal basic = 3000m)
    {
        var database = $"runcmd_{Guid.NewGuid():N}";
        await using var db = fx.NewDbOn(database);
        await db.Database.MigrateAsync();
        await SeedData.SeedDemoUsersAsync(db, new PasswordHasher());
        if (verifyRates)
            foreach (var row in await db.StatutoryRateTables.ToListAsync())
                row.Verify("accountant", DateTime.UtcNow, "checked");

        var clerk = Role.Create("PAYROLL CLERK");
        var head = Role.Create("HEAD OF HR");
        db.Roles.AddRange(clerk, head);
        await db.SaveChangesAsync();
        db.RolePermissions.Add(RolePermission.Create(clerk.Id, AppConstants.Permissions.PayrollManage));
        db.RolePermissions.Add(RolePermission.Create(head.Id, AppConstants.Permissions.PayrollApprove));
        db.RolePermissions.Add(RolePermission.Create(head.Id, AppConstants.Permissions.PayrollLock));
        db.RolePermissions.Add(RolePermission.Create(head.Id, AppConstants.Permissions.PayrollReopen));
        db.RolePermissions.Add(RolePermission.Create(head.Id, AppConstants.Permissions.PayrollManage));   // so the race tests can have two preparers
        var prep = Staff.Create("PREP", "Pre", "Parer", null, "hash", [clerk.Id]);
        var hohr = Staff.Create("HOHR", "Head", "Hr", null, "hash", [head.Id]);
        var alice = Staff.Create("ALICE", "Alice", "Tan", null, "hash", [], dateOfBirth: new DateTime(1990, 3, 4));
        alice.SetEmployment("citizen", new DateOnly(2024, 1, 1), null);
        db.Staff.AddRange(prep, hohr, alice);
        db.SalaryProfiles.Add(SalaryProfile.Create(alice.Id, new DateOnly(2020, 1, 1), basic, "monthly", null, null, null));
        await db.SaveChangesAsync();
        return new People(prep.Id, hohr.Id, alice.Id, database);
    }

    private static PayrollActor Act(Guid id, string name) => new(id, name);

    private static async Task<long> VersionAsync(AppDbContext db, Guid runId) => (await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId)).Version;

    [Fact]
    public async Task The_whole_lifecycle_runs_on_a_real_database_and_a_repayment_is_stored_encrypted()
    {
        var p = await SeedAsync(basic: 3000m);
        await using var db = fx.NewRetryingDbOn(p.Database);
        var audit = new Audit();
        var clock = new Clock();
        var prep = Act(p.Prep, "Pre Parer");
        var hohr = Act(p.Hohr, "Head Hr");
        var advance = CashAdvance.Create(p.Alice, 500m, new DateOnly(2026, 9, 2), "Rent", "admin");
        db.CashAdvances.Add(advance);
        await db.SaveChangesAsync();

        var id = await new StartPayrollRunCommandHandler(db, audit, clock).Handle(new StartPayrollRunCommand(2026, 10, prep), CancellationToken.None);
        await new SubmitPayrollRunCommandHandler(db, audit, clock).Handle(new SubmitPayrollRunCommand(id, await VersionAsync(db, id), prep), CancellationToken.None);
        Assert.Equal(1, await new SignOffAllPayrollLinesCommandHandler(db, audit, clock).Handle(new SignOffAllPayrollLinesCommand(id, await VersionAsync(db, id), hohr), CancellationToken.None));
        await new ApprovePayrollRunCommandHandler(db, audit, clock).Handle(new ApprovePayrollRunCommand(id, await VersionAsync(db, id), hohr), CancellationToken.None);
        await new LockPayrollRunCommandHandler(db, audit, clock).Handle(new LockPayrollRunCommand(id, await VersionAsync(db, id), hohr), CancellationToken.None);

        Assert.Equal(PayrollRunState.Locked, (await db.PayrollRuns.AsNoTracking().SingleAsync()).State);
        Assert.Equal(CashAdvance.Settled, (await db.CashAdvances.AsNoTracking().SingleAsync()).Status);
        var raw = await db.Database.SqlQueryRaw<string>("""SELECT "Amount" AS "Value" FROM "CashAdvanceRepayments" """).SingleAsync();
        Assert.StartsWith("enc1:", raw);
        Assert.Equal(["Started", "Submitted", "SignedOffAll", "Approved", "Locked"], audit.Actions.Where(a => a != "LineEdited"));

        await new ReopenPayrollRunCommandHandler(db, audit, clock).Handle(new ReopenPayrollRunCommand(id, await VersionAsync(db, id), hohr), CancellationToken.None);

        Assert.Equal(PayrollRunState.Draft, (await db.PayrollRuns.AsNoTracking().SingleAsync()).State);
        Assert.Equal(CashAdvance.Outstanding, (await db.CashAdvances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, await db.CashAdvanceRepayments.CountAsync());
        Assert.Equal(0, await db.PayrollLocks.CountAsync());
    }

    /// <summary>WHY: two people click Start for the same month at the same moment. One run exists afterwards and the loser is told so cleanly, never a database error.</summary>
    [Fact]
    public async Task Two_people_starting_the_same_month_at_once_end_with_one_run_and_a_clear_message_for_the_other()
    {
        var p = await SeedAsync();
        await using var first = fx.NewRetryingDbOn(p.Database);
        await using var second = fx.NewRetryingDbOn(p.Database);
        var clock = new Clock();

        var results = await Task.WhenAll(
            Capture(() => new StartPayrollRunCommandHandler(first, new Audit(), clock).Handle(new StartPayrollRunCommand(2026, 10, Act(p.Prep, "Pre Parer")), CancellationToken.None)),
            Capture(() => new StartPayrollRunCommandHandler(second, new Audit(), clock).Handle(new StartPayrollRunCommand(2026, 10, Act(p.Hohr, "Head Hr")), CancellationToken.None)));

        Assert.Equal(1, results.Count(r => r is null));
        Assert.IsType<InvalidOperationException>(Assert.Single(results, r => r is not null));
        await using var check = fx.NewDbOn(p.Database);
        Assert.Equal(1, await check.PayrollRuns.CountAsync());
    }

    [Fact]
    public async Task Two_edits_from_the_same_version_leave_one_winner_and_the_other_is_told_to_refresh()
    {
        var p = await SeedAsync();
        await using var seed = fx.NewRetryingDbOn(p.Database);
        var clock = new Clock();
        var id = await new StartPayrollRunCommandHandler(seed, new Audit(), clock).Handle(new StartPayrollRunCommand(2026, 10, Act(p.Prep, "Pre Parer")), CancellationToken.None);
        var version = await VersionAsync(seed, id);
        await using var a = fx.NewRetryingDbOn(p.Database);
        await using var b = fx.NewRetryingDbOn(p.Database);

        var results = await Task.WhenAll(
            Capture(() => new UpdatePayrollRunLineCommandHandler(a, new Audit(), clock).Handle(new UpdatePayrollRunLineCommand(id, p.Alice, 100m, false, null, version, Act(p.Prep, "Pre Parer")), CancellationToken.None)),
            Capture(() => new UpdatePayrollRunLineCommandHandler(b, new Audit(), clock).Handle(new UpdatePayrollRunLineCommand(id, p.Alice, 200m, false, null, version, Act(p.Hohr, "Head Hr")), CancellationToken.None)));

        Assert.Equal(1, results.Count(r => r is null));
        var loser = Assert.IsType<InvalidOperationException>(Assert.Single(results, r => r is not null));
        Assert.Contains("someone else", loser.Message);
    }

    [Fact]
    public async Task Approval_is_refused_on_a_real_database_while_the_seeded_rates_are_unverified()
    {
        var p = await SeedAsync(verifyRates: false);
        await using var db = fx.NewRetryingDbOn(p.Database);
        var audit = new Audit();
        var clock = new Clock();
        var prep = Act(p.Prep, "Pre Parer");
        var hohr = Act(p.Hohr, "Head Hr");
        var id = await new StartPayrollRunCommandHandler(db, audit, clock).Handle(new StartPayrollRunCommand(2026, 10, prep), CancellationToken.None);
        await new SubmitPayrollRunCommandHandler(db, audit, clock).Handle(new SubmitPayrollRunCommand(id, await VersionAsync(db, id), prep), CancellationToken.None);
        await new SignOffAllPayrollLinesCommandHandler(db, audit, clock).Handle(new SignOffAllPayrollLinesCommand(id, await VersionAsync(db, id), hohr), CancellationToken.None);

        var version = await VersionAsync(db, id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ApprovePayrollRunCommandHandler(db, audit, clock).Handle(new ApprovePayrollRunCommand(id, version, hohr), CancellationToken.None));

        Assert.Contains("EPF (citizen)", ex.Message);
        Assert.Equal(PayrollRunState.Submitted, (await db.PayrollRuns.AsNoTracking().SingleAsync()).State);
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }
}
