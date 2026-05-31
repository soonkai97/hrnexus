using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Proves the concurrency guards actually win real races against PostgreSQL — the xmin optimistic
/// lock, the partial unique indexes, the tips unique constraint, and the Serializable balance check.
/// Each test runs genuine parallel operations on SEPARATE DbContexts (real connections), which is
/// the only way to exercise transaction isolation and DB-level constraints.
/// </summary>
[Collection("postgres")]
public class ConcurrencyTests(PostgresFixture fx)
{
    // ── Stubs ─────────────────────────────────────────────────────────────────
    private sealed class NullHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default) => Task.CompletedTask;
        public Task BroadcastLiveStatsAsync(LiveStatsDto s, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class NullPush : IPushNotificationService
    {
        public Task SendAsync(Guid id, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendToManyAsync(IEnumerable<Guid> ids, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => UtcNow;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────
    private static E.Staff MakeStaff(string staffId) =>
        E.Staff.Create(staffId, "Race", "Tester", $"{staffId}@t.com", "hash", new List<string> { "BARTENDER" });

    private async Task<Guid> SeedStaffWithRoleAsync(string staffId)
    {
        await using var db = fx.NewDb();
        if (!await db.Roles.AnyAsync(r => r.Name == "BARTENDER"))
            db.Roles.Add(E.Role.Create("bartender", hourlyRate: 20m));
        var staff = MakeStaff(staffId);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
    }

    // ── Fix #2: WorkLog xmin optimistic lock ──────────────────────────────────────
    [Fact]
    public async Task Xmin_BlocksConcurrentWorkLogUpdate_SecondSaveThrows()
    {
        var empId = await SeedStaffWithRoleAsync("XMIN01");
        Guid logId;
        await using (var seed = fx.NewDb())
        {
            var wl = E.WorkLog.CreateManual(empId, "BARTENDER", "loc1", "Main",
                new DateOnly(2026, 1, 1), "09:00", "17:00", 20m, "original");
            seed.WorkLogs.Add(wl);
            await seed.SaveChangesAsync();
            logId = wl.Id;
        }

        // Two contexts load the SAME row (same xmin snapshot)…
        await using var ctx1 = fx.NewDb();
        await using var ctx2 = fx.NewDb();
        var w1 = await ctx1.WorkLogs.FirstAsync(w => w.Id == logId);
        var w2 = await ctx2.WorkLogs.FirstAsync(w => w.Id == logId);

        w1.Update("BARTENDER", "loc1", "Main", new DateOnly(2026, 1, 1), "09:00", "17:00", "edit-by-1");
        await ctx1.SaveChangesAsync();   // bumps xmin

        // …ctx2 still holds the stale xmin → its UPDATE matches 0 rows → concurrency exception.
        w2.Update("BARTENDER", "loc1", "Main", new DateOnly(2026, 1, 1), "09:00", "17:00", "edit-by-2");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync());
    }

    // ── Double clock-in: partial unique index IX_WorkLogs_EmployeeId_Active ────────
    [Fact]
    public async Task ActiveShiftUniqueIndex_BlocksSecondConcurrentClockIn()
    {
        var empId = await SeedStaffWithRoleAsync("CLOCK01");

        await using var ctx1 = fx.NewDb();
        await using var ctx2 = fx.NewDb();
        ctx1.WorkLogs.Add(E.WorkLog.StartLive(empId, "BARTENDER", "loc1", "Main", new DateOnly(2026, 1, 1), "09:00", 20m));
        ctx2.WorkLogs.Add(E.WorkLog.StartLive(empId, "BARTENDER", "loc1", "Main", new DateOnly(2026, 1, 1), "10:00", 20m));

        await ctx1.SaveChangesAsync();   // first active shift inserts fine
        // Second active shift for the same employee violates the partial unique index (EndTime IS NULL).
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());

        await using var verify = fx.NewDb();
        Assert.Equal(1, await verify.WorkLogs.CountAsync(w => w.EmployeeId == empId && w.EndTime == null));
    }


    // ── Fix #1: balance overrun under Serializable isolation ──────────────────────
    [Fact]
    public async Task BalanceOverrun_SerializableForcesExactlyOneApprovalToFail()
    {
        var empId = await SeedStaffWithRoleAsync("LEAVE01");
        await using (var seed = fx.NewDb())
        {
            // Entitlement = 8h. Each request below is 5h → fits alone (5 ≤ 8) but NOT together (10 > 8).
            seed.LeaveEntitlements.Add(E.LeaveEntitlement.Create(empId, 2026, annualHours: 8m));
            await seed.SaveChangesAsync();
        }

        // Different date ranges so the OVERLAP check never trips — the BALANCE check is the only
        // thing that can reject, which is exactly what we want to stress under concurrency.
        UpsertLeaveCommand Cmd(DateTime start) => new(
            Id: null, EmployeeId: empId, LeaveType: AppConstants.Leave.Annual,
            StartDate: start, EndDate: start, StartTime: null, EndTime: null,
            Hours: 5m, Notes: null, Status: AppConstants.Leave.Approved,
            RejectionReason: null, SkipAdvanceNotice: true);

        async Task<Exception?> Run(DateTime start)
        {
            await using var db = fx.NewDb();
            var handler = new UpsertLeaveCommandHandler(db, new NullHub(), new NullPush(), new NullAudit(), new FixedClock());
            try { await handler.Handle(Cmd(start), CancellationToken.None); return null; }
            catch (Exception ex) { return ex; }
        }

        // Fire both at once — genuine overlap so Serializable SSI must arbitrate.
        var results = await Task.WhenAll(
            Run(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
            Run(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)));

        var failures  = results.Count(r => r != null);
        var successes = results.Count(r => r == null);

        Assert.Equal(1, successes);     // exactly one approval committed
        Assert.Equal(1, failures);      // the other was rejected/aborted

        // Invariant: the DB never holds more approved annual hours than the entitlement allows.
        await using var verify = fx.NewDb();
        var approvedHours = await verify.LeaveRequests
            .Where(l => l.EmployeeId == empId && l.LeaveType == AppConstants.Leave.Annual
                     && l.Status == AppConstants.Leave.Approved)
            .SumAsync(l => l.Hours ?? 0m);
        Assert.True(approvedHours <= 8m, $"Overdraw! approved={approvedHours}h exceeds 8h entitlement.");
    }
}
