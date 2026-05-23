using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Admin.Commands;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Application.Leave.Commands;
using NightclubWorkforce.Application.Tests.Infrastructure;
using E = NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

/// <summary>
/// Handler-level tests for Leave commands:
/// CancelLeave, UpsertLeave (admin), DeleteLeave, UpsertLeaveEntitlement.
/// Guards business rules around leave status, balances, date ranges, and overlap.
/// </summary>
public class LeaveCommandTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class NullNotificationHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default) => Task.CompletedTask;
        public Task BroadcastLiveStatsAsync(LiveStatsDto s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullPushSvc : IPushNotificationService
    {
        public Task SendAsync(Guid id, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendToManyAsync(IEnumerable<Guid> ids, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullAuditService : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubDateTimeProvider : IDateTimeProvider
    {
        private readonly DateTime _now;
        public StubDateTimeProvider(DateTime? now = null) => _now = now ?? DateTime.UtcNow;
        public DateTime UtcNow => _now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(_now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => _now;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static E.Staff MakeStaff(string id = "S001")
        => E.Staff.Create(id, "Alice", "Wong", "alice@test.com", "hash",
            new List<string> { "BARTENDER" });

    private static E.LeaveRequest MakeLeave(Guid empId, string status = "pending",
        DateTime? start = null, DateTime? end = null)
    {
        var s = start ?? DateTime.UtcNow.Date;
        var e = end   ?? DateTime.UtcNow.Date.AddDays(2);
        var leave = E.LeaveRequest.Create(empId, "annual", s, e, null, null, null, null);
        if (status != "pending")
            leave.Update("annual", s, e, null, null, null, null, status);
        return leave;
    }

    // ── CancelLeaveCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task CancelLeave_ThrowsKeyNotFound_WhenLeaveNotFoundOrWrongEmployee()
    {
        await using var db = CreateDb();
        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new CancelLeaveCommand(Guid.NewGuid(), Guid.NewGuid()),
                CancellationToken.None));
    }

    [Fact]
    public async Task CancelLeave_ThrowsInvalidOperation_WhenLeaveRejected()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        var leave = MakeLeave(staff.Id, status: "rejected");
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CancelLeave_ThrowsInvalidOperation_WhenLeaveAlreadyEnded()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        var yesterday = DateTime.UtcNow.Date.AddDays(-2);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", yesterday, yesterday, null, null, null, null);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // "Now" is today, leave ended yesterday
        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CancelLeave_Succeeds_SetsCancelledStatus_ForPendingLeave()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", tomorrow, tomorrow.AddDays(2), null, null, null, null);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt);
        await handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None);

        var updated = await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id);
        Assert.Equal("cancelled", updated.Status);

        // Audit log should be created
        Assert.Equal(1, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task CancelLeave_Succeeds_ForApprovedLeave_CreatesAuditLog()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var leave = MakeLeave(staff.Id, status: "approved", start: tomorrow, end: tomorrow.AddDays(2));
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt);
        await handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None);

        var updated = await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id);
        Assert.Equal("cancelled", updated.Status);
        var auditLog = await db.AuditLogs.FirstAsync();
        Assert.Equal("WithdrawnApproved", auditLog.Action);
    }

    // ── UpsertLeaveCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertLeave_ThrowsArgument_WhenInvalidLeaveType()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(null, staff.Id, "INVALID",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, null, null, null, "pending");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_ThrowsArgument_WhenStartAfterEnd()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var tomorrow = DateTime.UtcNow.AddDays(1);
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "annual",
            tomorrow, yesterday, null, null, null, null, "pending");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_ThrowsArgument_WhenHoursExceed720()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Use "sick" to avoid the annual 3-day advance rule interfering
        var start = DateTime.UtcNow;
        var cmd = new UpsertLeaveCommand(null, staff.Id, "sick",
            start, start.AddDays(30), null, null, 721m, null, "pending");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_ThrowsArgument_WhenMoreThan1YearInAdvance()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var now = new DateTime(2026, 1, 1);
        var dt = new StubDateTimeProvider(now);
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Start is > 1 year in future from dt.LocalNow
        var farFuture = now.AddYears(1).AddDays(2);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "annual",
            farFuture, farFuture.AddDays(1), null, null, 8m, null, "pending");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_ThrowsInvalidOperation_WhenInsufficientAnnualBalance()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        // Set entitlement to 2 hours only
        var ent = E.LeaveEntitlement.Create(staff.Id, DateTime.UtcNow.Year, annualHours: 2m, sickHours: 14m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Request 5 hours (exceeds 2h entitlement) — use start ≥ 3 days to pass advance-notice rule
        var start = DateTime.UtcNow.AddDays(5);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "annual",
            start, start.AddDays(1), null, null, 5m, null, "pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_ThrowsInvalidOperation_WhenOverlapExists()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        var existingLeave = E.LeaveRequest.Create(staff.Id, "annual",
            DateTime.UtcNow.AddDays(5), DateTime.UtcNow.AddDays(10), null, null, null, null);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(existingLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Overlapping range
        var start = DateTime.UtcNow.AddDays(7);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "unpaid",
            start, start.AddDays(2), null, null, 8m, null, "pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_Create_Succeeds_ForUnpaidLeave()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var start = DateTime.UtcNow.AddDays(1);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "unpaid",
            start, start.AddDays(2), null, null, 8m, "reason", "pending");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, leaveId);
        var created = await db.LeaveRequests.FirstAsync(l => l.Id == leaveId);
        Assert.Equal("unpaid", created.LeaveType);
    }

    [Fact]
    public async Task UpsertLeave_Update_Succeeds_ChangesStatus()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        var start = DateTime.UtcNow.AddDays(5);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", start, start.AddDays(1), null, null, 8m, null);
        // Entitlement required — handler checks balance before the FindAsync lookup
        var ent = E.LeaveEntitlement.Create(staff.Id, start.Year, annualHours: 40m, sickHours: 40m);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(leave.Id, staff.Id, "annual",
            start, start.AddDays(1), null, null, 8m, null, "approved");

        await handler.Handle(cmd, CancellationToken.None);

        var updated = await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id);
        Assert.Equal("approved", updated.Status);
    }

    [Fact]
    public async Task UpsertLeave_Update_ThrowsKeyNotFound_WhenLeaveNotFound()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Use "rejected" status — balance check is skipped for rejections, so the
        // handler reaches the FindAsync lookup and throws KeyNotFoundException.
        var cmd = new UpsertLeaveCommand(Guid.NewGuid(), staff.Id, "annual",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, null, 8m, null, "rejected");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    // ── Annual leave advance-notice rule ──────────────────────────────────────

    [Fact]
    public async Task UpsertLeave_ThrowsArgument_WhenAnnualLeaveWithinAdvanceWindow()
    {
        // Annual leave starting tomorrow (< 3 days) must be rejected
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var dt  = new StubDateTimeProvider(now);
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var tomorrow = now.AddDays(1);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "annual",
            tomorrow, tomorrow.AddDays(1), null, null, 8m, null, "pending");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));
        Assert.Contains("3 days in advance", ex.Message);
    }

    [Fact]
    public async Task UpsertLeave_AnnualLeave_Succeeds_WhenSufficientAdvanceNotice()
    {
        // Annual leave starting in 4 days should pass the advance-notice rule
        await using var db = CreateDb();
        var staff = MakeStaff();
        var start = DateTime.UtcNow.AddDays(4);
        var ent = E.LeaveEntitlement.Create(staff.Id, start.Year, annualHours: 40m, sickHours: 40m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(null, staff.Id, "annual",
            start, start.AddDays(1), null, null, 8m, null, "pending");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, leaveId);
    }

    [Fact]
    public async Task UpsertLeave_AnnualLeaveUpdate_NotBlockedByAdvanceRule()
    {
        // Updating an existing annual leave record (Id.HasValue) skips the advance-notice check
        await using var db = CreateDb();
        var staff = MakeStaff();
        // Leave starting tomorrow — would fail if it were a new request
        var start = DateTime.UtcNow.AddDays(1);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", start, start.AddDays(1), null, null, 8m, null);
        var ent = E.LeaveEntitlement.Create(staff.Id, start.Year, annualHours: 40m, sickHours: 40m);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Approve an existing leave — update should succeed even though start is < 3 days away
        var cmd = new UpsertLeaveCommand(leave.Id, staff.Id, "annual",
            start, start.AddDays(1), null, null, 8m, null, "approved");

        await handler.Handle(cmd, CancellationToken.None);

        var updated = await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id);
        Assert.Equal("approved", updated.Status);
    }

    // ── Emergency leave ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertLeave_Emergency_IsAutoApproved_WhenSubmittedAsPending()
    {
        // Emergency leave submitted with "pending" must be stored as "approved"
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var today = DateTime.UtcNow;
        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            today, today, null, null, 8m, "urgent family matter", "pending");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);

        var created = await db.LeaveRequests.FirstAsync(l => l.Id == leaveId);
        Assert.Equal("emergency", created.LeaveType);
        Assert.Equal("approved", created.Status);
    }

    [Fact]
    public async Task UpsertLeave_Emergency_SkipsBalanceCheck_NoEntitlementNeeded()
    {
        // Emergency leave bypasses entitlement checks — no annual/sick quota consumed
        await using var db = CreateDb();
        var staff = MakeStaff();
        // No entitlement seeded — would block annual or sick leave
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var today = DateTime.UtcNow;
        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            today, today, null, null, 8m, null, "pending");

        // Should succeed even with no entitlement
        var leaveId = await handler.Handle(cmd, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, leaveId);
    }

    [Fact]
    public async Task UpsertLeave_Emergency_CanBeAppliedSameDay_NoAdvanceRequired()
    {
        // Emergency leave is not subject to the annual 3-day advance rule
        await using var db = CreateDb();
        var staff = MakeStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var dt  = new StubDateTimeProvider(now);
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Same day — would fail for annual
        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            now, now, null, null, 4m, null, "pending");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, leaveId);
    }

    // ── DeleteLeaveCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteLeave_ThrowsKeyNotFound_WhenLeaveNotFound()
    {
        await using var db = CreateDb();
        var handler = new DeleteLeaveCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteLeaveCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteLeave_Succeeds_RemovesLeave()
    {
        await using var db = CreateDb();
        var staff = MakeStaff();
        var leave = MakeLeave(staff.Id);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteLeaveCommandHandler(db);
        await handler.Handle(new DeleteLeaveCommand(leave.Id), CancellationToken.None);

        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    // ── UpsertLeaveEntitlementCommand ─────────────────────────────────────────

    [Fact]
    public async Task UpsertLeaveEntitlement_ThrowsArgument_WhenNegativeHours()
    {
        await using var db = CreateDb();
        var handler = new UpsertLeaveEntitlementCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new UpsertLeaveEntitlementCommand(Guid.NewGuid(), 2026, -1m, 14m),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeaveEntitlement_ThrowsArgument_WhenHoursExceed8760()
    {
        await using var db = CreateDb();
        var handler = new UpsertLeaveEntitlementCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new UpsertLeaveEntitlementCommand(Guid.NewGuid(), 2026, 8761m, 14m),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeaveEntitlement_ThrowsArgument_WhenYearTooOld()
    {
        await using var db = CreateDb();
        var handler = new UpsertLeaveEntitlementCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new UpsertLeaveEntitlementCommand(Guid.NewGuid(), 1999, 14m, 14m),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeaveEntitlement_Create_Succeeds()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var handler = new UpsertLeaveEntitlementCommandHandler(db);

        await handler.Handle(new UpsertLeaveEntitlementCommand(empId, 2026, 21m, 10m),
            CancellationToken.None);

        var ent = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == empId);
        Assert.Equal(21m, ent.AnnualHours);
        Assert.Equal(10m, ent.SickHours);
    }

    [Fact]
    public async Task UpsertLeaveEntitlement_Update_Succeeds_WhenEntitlementExists()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        var existing = E.LeaveEntitlement.Create(empId, 2026, 14m, 14m);
        db.LeaveEntitlements.Add(existing);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpsertLeaveEntitlementCommandHandler(db);
        await handler.Handle(new UpsertLeaveEntitlementCommand(empId, 2026, 20m, 7m),
            CancellationToken.None);

        var updated = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == empId);
        Assert.Equal(20m, updated.AnnualHours);
        Assert.Equal(7m, updated.SickHours);
    }
}
