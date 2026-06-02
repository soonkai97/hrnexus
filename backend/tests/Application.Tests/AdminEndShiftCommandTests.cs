using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Application.WorkLog.Commands;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// AdminEndShiftCommand — a manager/admin closing an OPEN shift (forgotten clock-out) at a chosen
/// end time. Verifies the guards that the staff-only EndWorkLogCommand and the
/// active-shift-refusing UpdateWorkLogCommand don't cover.
/// </summary>
public class AdminEndShiftCommandTests
{
    private sealed class NullNotificationHub : INotificationHub
    {
        public Task SendToUserAsync(Guid employeeId, NotificationPayload payload, CancellationToken ct = default) => Task.CompletedTask;
        public Task BroadcastLiveStatsAsync(LiveStatsDto stats, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullSender : MediatR.ISender
    {
        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult(default(TResponse)!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : MediatR.IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static AdminEndShiftCommandHandler Handler(IAppDbContext db) =>
        new(db, new NullSender(), new NullNotificationHub());

    [Fact]
    public async Task Closes_an_open_shift_at_the_chosen_end_time()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var workDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var log = E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", workDate, "18:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new AdminEndShiftCommand(log.Id, Guid.NewGuid(), "23:00"), CancellationToken.None);

        var updated = await db.WorkLogs.FindAsync(log.Id);
        Assert.Equal("23:00", updated!.EndTime);   // shift is now closed → drops off the on-shift list
    }

    [Fact]
    public async Task Throws_KeyNotFound_when_the_shift_does_not_exist()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Handler(db).Handle(new AdminEndShiftCommand(Guid.NewGuid(), Guid.NewGuid(), "23:00"), CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_the_shift_is_already_ended()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var workDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var log = E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", workDate, "18:00", "23:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new AdminEndShiftCommand(log.Id, Guid.NewGuid(), "23:30"), CancellationToken.None));
    }

    [Fact]
    public async Task Throws_on_invalid_end_time_format()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var workDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var log = E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", workDate, "18:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(new AdminEndShiftCommand(log.Id, Guid.NewGuid(), "99:99"), CancellationToken.None));
    }

    [Fact]
    public async Task Identical_start_and_end_is_treated_as_24h_not_rejected()
    {
        // Per ShiftHours.CalcHours, identical times yield 24h (a full overnight shift), not 0 — the
        // same intentional behaviour as EndWorkLogCommand. So this saves normally rather than throwing.
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var workDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var log = E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", workDate, "18:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new AdminEndShiftCommand(log.Id, Guid.NewGuid(), "18:00"), CancellationToken.None);

        var ended = await db.WorkLogs.FindAsync(log.Id);
        Assert.Equal("18:00", ended!.EndTime);
    }

    [Fact]
    public async Task Refuses_to_close_a_shift_in_a_locked_payroll_period()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 5, 10);
        var log = E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", workDate, "18:00", 50m);
        db.WorkLogs.Add(log);
        db.PayrollLocks.Add(E.PayrollLock.Create(2026, 5, "Mgr"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new AdminEndShiftCommand(log.Id, Guid.NewGuid(), "23:00"), CancellationToken.None));
    }
}
