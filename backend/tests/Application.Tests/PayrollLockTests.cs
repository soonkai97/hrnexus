using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Month-lock feature: locking a payroll period freezes its shift logs.
/// Covers the lock/reopen/query handlers and that the PayrollLockGuard blocks mutations to a
/// locked month.
/// </summary>
public class PayrollLockTests
{
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static TestDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // ── Lock / Reopen / Query ─────────────────────────────────────────────────

    [Fact]
    public async Task Lock_CreatesRow_AndRejectsLockingTheSamePeriodTwice()
    {
        await using var db = MakeDb();
        var handler = new LockPayrollPeriodCommandHandler(db, new NullAudit());

        await handler.Handle(new LockPayrollPeriodCommand(2026, 5, "Manager Mei"), CancellationToken.None);
        Assert.True(await db.PayrollLocks.AnyAsync(p => p.Year == 2026 && p.Month == 5));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new LockPayrollPeriodCommand(2026, 5, "Manager Mei"), CancellationToken.None));
    }

    [Fact]
    public async Task Lock_ThrowsArgument_ForInvalidMonth()
    {
        await using var db = MakeDb();
        var handler = new LockPayrollPeriodCommandHandler(db, new NullAudit());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new LockPayrollPeriodCommand(2026, 13, "Mgr"), CancellationToken.None));
    }

    [Fact]
    public async Task Reopen_RemovesLock_AndThrowsWhenNotLocked()
    {
        await using var db = MakeDb();
        db.PayrollLocks.Add(E.PayrollLock.Create(2026, 5, "Mgr"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reopen = new ReopenPayrollPeriodCommandHandler(db, new NullAudit());
        await reopen.Handle(new ReopenPayrollPeriodCommand(2026, 5, "Mgr"), CancellationToken.None);
        Assert.False(await db.PayrollLocks.AnyAsync());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            reopen.Handle(new ReopenPayrollPeriodCommand(2026, 5, "Mgr"), CancellationToken.None));
    }

    [Fact]
    public async Task GetPayrollLocks_ReturnsNewestFirst()
    {
        await using var db = MakeDb();
        db.PayrollLocks.Add(E.PayrollLock.Create(2026, 3, "Mgr"));
        db.PayrollLocks.Add(E.PayrollLock.Create(2026, 5, "Mgr"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetPayrollLocksQueryHandler(db).Handle(new GetPayrollLocksQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(5, result[0].Month);   // 2026-05 before 2026-03
        Assert.Equal(3, result[1].Month);
    }

    // ── Guard ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_Throws_WhenMonthLocked_AndPasses_WhenOpen()
    {
        await using var db = MakeDb();
        db.PayrollLocks.Add(E.PayrollLock.Create(2026, 5, "Mgr"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PayrollLockGuard.EnsureMonthOpenAsync(db, 2026, 5, CancellationToken.None));

        // A different (open) month is fine.
        await PayrollLockGuard.EnsureMonthOpenAsync(db, 2026, 6, CancellationToken.None);
    }
}
