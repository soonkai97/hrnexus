using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Proves the concurrency guards actually win real races against PostgreSQL — the xmin optimistic
/// lock and the Serializable balance check.
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
    private async Task<Guid> SeedStaffWithRoleAsync(string staffId)
    {
        await using var db = fx.NewDb();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "BARTENDER");
        if (role is null)
        {
            role = E.Role.Create("bartender", hourlyRate: 20m);
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        var staff = E.Staff.Create(staffId, "Race", "Tester", $"{staffId}@t.com", "hash",
            new List<Guid> { role.Id });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
    }

    // ── xmin optimistic lock (LeaveRequest) ───────────────────────────────────────
    [Fact]
    public async Task Xmin_BlocksConcurrentLeaveUpdate_SecondSaveThrows()
    {
        var empId = await SeedStaffWithRoleAsync("XMIN01");
        var day = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        Guid leaveId;
        await using (var seed = fx.NewDb())
        {
            var lr = E.LeaveRequest.Create(empId, AppConstants.Leave.Unpaid, day, day, null, null, 8m, "original");
            seed.LeaveRequests.Add(lr);
            await seed.SaveChangesAsync();
            leaveId = lr.Id;
        }

        // Two contexts load the SAME row (same xmin snapshot)…
        await using var ctx1 = fx.NewDb();
        await using var ctx2 = fx.NewDb();
        var l1 = await ctx1.LeaveRequests.FirstAsync(l => l.Id == leaveId);
        var l2 = await ctx2.LeaveRequests.FirstAsync(l => l.Id == leaveId);

        l1.Update(AppConstants.Leave.Unpaid, day, day, null, null, 8m, "edit-by-1", AppConstants.Leave.Approved);
        await ctx1.SaveChangesAsync();   // bumps xmin

        // …ctx2 still holds the stale xmin → its UPDATE matches 0 rows → concurrency exception.
        l2.Update(AppConstants.Leave.Unpaid, day, day, null, null, 8m, "edit-by-2", AppConstants.Leave.Rejected);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync());
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
            StartDate: start, EndDate: start, StartTime: "09:00", EndTime: "14:00",
            Hours: null, Notes: null, Status: AppConstants.Leave.Approved,
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

    // ── Phase 7l (D5): the same guarantee when requests straddle New Year ────────
    [Fact]
    public async Task CrossYearBalance_SerializableStillAllowsOnlyOneOverdrawingRequest()
    {
        var empId = await SeedStaffWithRoleAsync("LEAVE02");
        await using (var seed = fx.NewDb())
        {
            // 2026 allows 28h; 2027 has plenty. Request A (Mon 28 to Wed 30 Dec) needs 24h of 2026.
            // Request B (Thu 31 Dec to Fri 1 Jan) needs 8h of 2026 and 8h of 2027. Each fits alone,
            // together they need 32h of 2026 (> 28h), so exactly one may commit.
            seed.LeaveEntitlements.Add(E.LeaveEntitlement.Create(empId, 2026, annualHours: 28m));
            seed.LeaveEntitlements.Add(E.LeaveEntitlement.Create(empId, 2027, annualHours: 100m));
            await seed.SaveChangesAsync();
        }

        UpsertLeaveCommand Cmd(DateTime start, DateTime end) => new(
            Id: null, EmployeeId: empId, LeaveType: AppConstants.Leave.Annual,
            StartDate: start, EndDate: end, StartTime: null, EndTime: null,
            Hours: null, Notes: null, Status: AppConstants.Leave.Approved,
            RejectionReason: null, SkipAdvanceNotice: true);

        async Task<Exception?> Run(DateTime start, DateTime end)
        {
            await using var db = fx.NewDb();
            var handler = new UpsertLeaveCommandHandler(db, new NullHub(), new NullPush(), new NullAudit(), new FixedClock());
            try { await handler.Handle(Cmd(start, end), CancellationToken.None); return null; }
            catch (Exception ex) { return ex; }
        }

        var results = await Task.WhenAll(
            Run(new DateTime(2026, 12, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc)),
            Run(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(1, results.Count(r => r == null));

        await using var verify = fx.NewDb();
        var leaves = await verify.LeaveRequests
            .Where(l => l.EmployeeId == empId && l.Status == AppConstants.Leave.Approved)
            .Select(l => new { l.StartDate, l.EndDate, l.Hours })
            .ToListAsync();
        var used2026 = leaves.Sum(l => HRNexus.Application.Leave.LeaveHoursCalculator.HoursInYear(l.StartDate, l.EndDate, l.Hours, 2026));
        Assert.True(used2026 <= 28m, $"Overdraw! {used2026}h of 2026 used, entitlement is 28h.");
    }
}
