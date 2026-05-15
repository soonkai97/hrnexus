using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Admin.Commands;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Application.Tests.Infrastructure;
using NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

/// <summary>
/// Tests for UpsertLeaveCommand overlap detection logic.
/// Uses an in-memory DbContext with null stubs for notification/push/audit services.
/// </summary>
public class LeaveOverlapTests
{
    // ── Null stubs ────────────────────────────────────────────────────────────

    private sealed class NullNotificationHub : INotificationHub
    {
        public Task SendToUserAsync(Guid employeeId, NotificationPayload payload, CancellationToken ct = default) => Task.CompletedTask;
        public Task BroadcastLiveStatsAsync(LiveStatsDto stats, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullPushService : IPushNotificationService
    {
        public Task SendAsync(Guid employeeId, string title, string body, string? url = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendToManyAsync(IEnumerable<Guid> employeeIds, string title, string body, string? url = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullAuditService : IAuditService
    {
        public Task LogAsync(string entityType, string entityId, string action,
            Guid? performedById, string performedByName, string? changes = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullDateTimeProvider : IDateTimeProvider
    {
        public DateTime     UtcNow        => DateTime.UtcNow;
        public DateOnly     TodayUtc      => DateOnly.FromDateTime(DateTime.UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Local;
        public DateTime     LocalNow      => DateTime.Now;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // isolated per test
            .Options;
        return new TestDbContext(opts);
    }

    private static UpsertLeaveCommandHandler CreateHandler(IAppDbContext db) =>
        new(db, new NullNotificationHub(), new NullPushService(), new NullAuditService(), new NullDateTimeProvider());

    private static DateTime Utc(int year, int month, int day) =>
        DateTime.SpecifyKind(new DateTime(year, month, day), DateTimeKind.Utc);

    /// <summary>Seeds an approved leave and saves it.</summary>
    private static async Task SeedLeave(TestDbContext db, Guid employeeId,
        DateTime start, DateTime end, string status = "approved", Guid? id = null)
    {
        var leave = LeaveRequest.Create(employeeId, "annual", start, end, null);
        if (status != "pending")
            leave.Update(leave.LeaveType, start, end, null, status);
        if (id.HasValue)
        {
            // Overwrite the random Id via reflection so we can reference it in update tests
            typeof(LeaveRequest)
                .GetProperty(nameof(LeaveRequest.Id))!
                .SetValue(leave, id.Value);
        }
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
    }

    // ── Create: no existing leaves ────────────────────────────────────────────

    [Fact]
    public async Task Create_NoExistingLeave_Succeeds()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);
        var empId = Guid.NewGuid();

        var result = await handler.Handle(
            new UpsertLeaveCommand(null, empId, "sick",
                Utc(2025, 6, 1), Utc(2025, 6, 5), null, "pending"),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result);
    }

    // ── Create: exact overlap throws ──────────────────────────────────────────

    [Fact]
    public async Task Create_ExactOverlap_Throws()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        await SeedLeave(db, empId, Utc(2025, 6, 1), Utc(2025, 6, 5));

        var handler = CreateHandler(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new UpsertLeaveCommand(null, empId, "sick",
                    Utc(2025, 6, 1), Utc(2025, 6, 5), null, "pending"),
                CancellationToken.None));
    }

    // ── Create: partial overlap (new starts inside existing) ─────────────────

    [Fact]
    public async Task Create_PartialOverlapStartsInside_Throws()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        await SeedLeave(db, empId, Utc(2025, 6, 1), Utc(2025, 6, 10));

        var handler = CreateHandler(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new UpsertLeaveCommand(null, empId, "sick",
                    Utc(2025, 6, 8), Utc(2025, 6, 15), null, "pending"),
                CancellationToken.None));
    }

    // ── Create: partial overlap (new ends inside existing) ───────────────────

    [Fact]
    public async Task Create_PartialOverlapEndsInside_Throws()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        await SeedLeave(db, empId, Utc(2025, 6, 5), Utc(2025, 6, 10));

        var handler = CreateHandler(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new UpsertLeaveCommand(null, empId, "sick",
                    Utc(2025, 6, 1), Utc(2025, 6, 7), null, "pending"),
                CancellationToken.None));
    }

    // ── Create: adjacent dates do NOT overlap ─────────────────────────────────

    [Fact]
    public async Task Create_AdjacentDates_Succeeds()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        await SeedLeave(db, empId, Utc(2025, 6, 1), Utc(2025, 6, 5));

        var handler = CreateHandler(db);
        // Starts the day after existing ends — should pass
        var result = await handler.Handle(
            new UpsertLeaveCommand(null, empId, "sick",
                Utc(2025, 6, 6), Utc(2025, 6, 10), null, "pending"),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result);
    }

    // ── Create: rejected leave does NOT block ─────────────────────────────────

    [Fact]
    public async Task Create_OverlapsRejectedLeave_Succeeds()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        // Seed a rejected leave in the same range
        await SeedLeave(db, empId, Utc(2025, 6, 1), Utc(2025, 6, 10), status: "rejected");

        var handler = CreateHandler(db);
        var result = await handler.Handle(
            new UpsertLeaveCommand(null, empId, "sick",
                Utc(2025, 6, 1), Utc(2025, 6, 10), null, "pending"),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result);
    }

    // ── Create: different employee is unaffected ──────────────────────────────

    [Fact]
    public async Task Create_AnotherEmployeesLeave_DoesNotBlock()
    {
        await using var db = CreateDb();
        var empA = Guid.NewGuid();
        var empB = Guid.NewGuid();
        await SeedLeave(db, empA, Utc(2025, 6, 1), Utc(2025, 6, 10));

        var handler = CreateHandler(db);
        var result = await handler.Handle(
            new UpsertLeaveCommand(null, empB, "sick",
                Utc(2025, 6, 1), Utc(2025, 6, 10), null, "pending"),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result);
    }

    // ── Create: start > end is rejected immediately ───────────────────────────

    [Fact]
    public async Task Create_StartAfterEnd_Throws()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);
        var empId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new UpsertLeaveCommand(null, empId, "sick",
                    Utc(2025, 6, 10), Utc(2025, 6, 1), null, "pending"),
                CancellationToken.None));
    }

    // ── Update: self-update does NOT block (same ID excluded) ─────────────────

    [Fact]
    public async Task Update_SameRecord_DoesNotSelfBlock()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var leaveId = Guid.NewGuid();
        await SeedLeave(db, empId, Utc(2025, 6, 1), Utc(2025, 6, 5),
            status: "pending", id: leaveId);

        var handler = CreateHandler(db);
        // Update same record: extend end date (same start, later end)
        var result = await handler.Handle(
            new UpsertLeaveCommand(leaveId, empId, "annual",
                Utc(2025, 6, 1), Utc(2025, 6, 10), null, "approved"),
            CancellationToken.None);

        Assert.Equal(leaveId, result);
    }
}
