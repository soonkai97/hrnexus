using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Scale tests: drive 50 genuinely-concurrent operations against real PostgreSQL to confirm the
/// guards hold under heavy contention (not just 2-way), and to surface throughput / retry-exhaustion
/// / connection-pool problems that only appear under load. The 2-way tests prove the race is closed;
/// these prove it stays closed — and the system stays correct — when 50 actors pile on at once.
/// </summary>
[Collection("postgres")]
public class LoadConcurrencyTests(PostgresFixture fx, ITestOutputHelper output)
{
    private const int N = 50;

    // ── stubs ─────────────────────────────────────────────────────────────────
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

    private async Task<List<Guid>> SeedStaffAsync(int count, string prefix)
    {
        await using var db = fx.NewDb();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "BARTENDER");
        if (role is null)
        {
            role = E.Role.Create("bartender");
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var s = E.Staff.Create($"{prefix}{i:D3}", "Load", $"User{i}", $"{prefix}{i}@t.com", "hash",
                new List<Guid> { role.Id });
            db.Staff.Add(s);
            ids.Add(s.Id);
        }
        await db.SaveChangesAsync();
        return ids;
    }

    // Run `count` thunks truly in parallel and collect the exception (or null) from each.
    private static async Task<Exception?[]> RunAllAsync(int count, Func<int, Task> body)
    {
        using var gate = new SemaphoreSlim(0, count);
        var results = new Exception?[count];
        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(async () =>
            {
                await gate.WaitAsync();           // hold every task at the start line…
                try { await body(idx); results[idx] = null; }
                catch (Exception ex) { results[idx] = ex; }
            });
        }
        gate.Release(count);                       // …then release them all at once
        await Task.WhenAll(tasks);
        return results;
    }

    // Runs a unit of work against a fresh context, returning ONLY the work's exception (or null).
    // Disposal errors are swallowed so connection-pool churn under load can't be mistaken for a
    // failed operation — we want to measure the handler's outcome, not teardown noise.
    private async Task WithDb(Func<AppDbContext, Task> work)
    {
        var db = fx.NewDb();
        try { await work(db); }
        finally { try { await db.DisposeAsync(); } catch { /* teardown noise under load */ } }
    }

    // ── Balance overrun under 50-way load → never exceeds entitlement ──────────
    [Fact]
    public async Task FiftyConcurrentApprovals_NeverOverdrawEntitlement_UnderHeavyContention()
    {
        var ids = await SeedStaffAsync(1, "BAL");
        var empId = ids[0];
        const decimal entitlement = 10m;   // 50 requests of 1h each compete for only 10h
        await using (var seed = fx.NewDb())
        {
            seed.LeaveEntitlements.Add(E.LeaveEntitlement.Create(empId, 2026, annualHours: entitlement));
            await seed.SaveChangesAsync();
        }

        var results = await RunAllAsync(N, i => WithDb(async db =>
        {
            var handler = new UpsertLeaveCommandHandler(db, new NullHub(), new NullPush(), new NullAudit(), new FixedClock());
            // Distinct, non-overlapping WEEKDAYS (Mon-Fri; leave is charged by working days) so ONLY
            // the balance check can reject. Each request is one hour (09:00-10:00) on its own day.
            var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i / 5 * 7 + i % 5);
            await handler.Handle(new UpsertLeaveCommand(
                Id: null, EmployeeId: empId, LeaveType: AppConstants.Leave.Annual,
                StartDate: start, EndDate: start, StartTime: "09:00", EndTime: "10:00",
                Hours: null, Notes: null, Status: AppConstants.Leave.Approved,
                RejectionReason: null, SkipAdvanceNotice: true), CancellationToken.None);
        }));

        var successes = results.Count(r => r == null);
        output.WriteLine($"50-way leave overrun: {successes} reported approved (cap {entitlement}h).");

        await using var verify = fx.NewDb();
        var approved = await verify.LeaveRequests
            .Where(l => l.EmployeeId == empId && l.LeaveType == AppConstants.Leave.Annual
                     && l.Status == AppConstants.Leave.Approved)
            .SumAsync(l => l.Hours ?? 0m);

        // (1) Safety: never approve more than the entitlement, no matter how many race in.
        Assert.True(approved <= entitlement, $"OVERDRAW: {approved}h approved exceeds {entitlement}h entitlement.");
        // (2) Capacity is used and progress is made (the cap isn't starving everyone out).
        Assert.InRange((int)approved, 1, (int)entitlement);
        // (3) Reporting matches reality: the count the callers were told "approved" EXACTLY equals
        //     the rows committed. The idempotency guard in UpsertLeaveCommand makes a retried-after-
        //     commit attempt return its own committed row instead of falsely reporting "insufficient".
        Assert.Equal((int)approved, successes);
    }
}
