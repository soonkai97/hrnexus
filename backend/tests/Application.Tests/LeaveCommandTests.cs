using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave.Commands;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

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

    /// <summary>Records the performer passed to the most recent LogAsync call so tests can assert
    /// who the audit entry is attributed to.</summary>
    private sealed class CapturingAuditService : IAuditService
    {
        public bool  Called      { get; private set; }
        public Guid? PerformedId { get; private set; }
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default)
        {
            Called = true; PerformedId = pid;
            return Task.CompletedTask;
        }
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

    /// <summary>Leave is charged by working days, so single-day tests must not land on a weekend.</summary>
    private static DateTime Weekday(DateTime d)
    {
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static async Task<E.Staff> MakeStaff(TestDbContext db, string id = "S001")
    {
        var bartender = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        return E.Staff.Create(id, "Alice", "Wong", "alice@test.com", "hash",
            new List<Guid> { bartender });
    }

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
        var handler = new CancelLeaveCommandHandler(db, dt, new NullNotificationHub());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new CancelLeaveCommand(Guid.NewGuid(), Guid.NewGuid()),
                CancellationToken.None));
    }

    [Fact]
    public async Task CancelLeave_ThrowsInvalidOperation_WhenLeaveRejected()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var leave = MakeLeave(staff.Id, status: "rejected");
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt, new NullNotificationHub());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CancelLeave_ThrowsInvalidOperation_WhenLeaveAlreadyEnded()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var yesterday = DateTime.UtcNow.Date.AddDays(-2);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", yesterday, yesterday, null, null, null, null);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // "Now" is today, leave ended yesterday
        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt, new NullNotificationHub());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CancelLeave_Succeeds_SetsCancelledStatus_ForPendingLeave()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", tomorrow, tomorrow.AddDays(2), null, null, null, null);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt, new NullNotificationHub());
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
        var staff = await MakeStaff(db);
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var leave = MakeLeave(staff.Id, status: "approved", start: tomorrow, end: tomorrow.AddDays(2));
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new CancelLeaveCommandHandler(db, dt, new NullNotificationHub());
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
    public async Task UpsertLeave_Create_AuditsWithNullPerformer_SoTheActualCallerIsAttributed()
    {
        // The create path must NOT credit the leave's subject employee as the performer — it passes
        // null so AuditService attributes the entry to the authenticated caller (admin or self).
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var audit = new CapturingAuditService();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), audit, new StubDateTimeProvider());

        var start = DateTime.UtcNow.AddDays(1);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "unpaid",
            start, start.AddDays(2), null, null, 8m, "reason", "pending");

        await handler.Handle(cmd, CancellationToken.None);

        Assert.True(audit.Called);
        Assert.Null(audit.PerformedId);   // null → AuditService fills in the real caller
    }

    [Fact]
    public async Task UpsertLeave_Update_Succeeds_ChangesStatus()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
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
        var staff = await MakeStaff(db);
        // Leave starting tomorrow — would fail if it were a new request (fixed clock: Mon 1 Jun 2026)
        var now   = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var start = now.AddDays(1);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", start, start.AddDays(1), null, null, 16m, null);
        var ent = E.LeaveEntitlement.Create(staff.Id, start.Year, annualHours: 40m, sickHours: 40m);
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(now);
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Approve an existing leave — update should succeed even though start is < 3 days away
        var cmd = new UpsertLeaveCommand(leave.Id, staff.Id, "annual",
            start, start.AddDays(1), null, null, 16m, null, "approved");

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
        var staff = await MakeStaff(db);
        var today = Weekday(DateTime.UtcNow);
        var ent = E.LeaveEntitlement.Create(staff.Id, today.Year, annualHours: 40m, sickHours: 14m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            today, today, null, null, 8m, "urgent family matter", "pending");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);

        var created = await db.LeaveRequests.FirstAsync(l => l.Id == leaveId);
        Assert.Equal("emergency", created.LeaveType);
        Assert.Equal("approved", created.Status);
    }

    [Fact]
    public async Task UpsertLeave_Emergency_DeductsFromAnnualBalance()
    {
        // Emergency leave uses the annual entitlement pool — both types count together
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var today = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);   // a Monday
        // Entitlement: 10h annual. Existing annual leave: 6h. Emergency request: 4h → exact fit.
        var ent = E.LeaveEntitlement.Create(staff.Id, today.Year, annualHours: 10m, sickHours: 14m);
        var existingAnnual = E.LeaveRequest.Create(staff.Id, "annual", today.AddDays(1), today.AddDays(1), null, null, 6m, null);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        db.LeaveRequests.Add(existingAnnual);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(today);
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // 4h emergency (09:00-13:00) + 6h annual = 10h (exactly at limit — should succeed)
        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            today, today, "09:00", "13:00", 4m, null, "pending");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, leaveId);
    }

    [Fact]
    public async Task UpsertLeave_Emergency_ThrowsWhenAnnualBalanceExceeded()
    {
        // Emergency leave is blocked when the annual entitlement pool is exhausted
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var today = Weekday(DateTime.UtcNow);
        // Only 2h annual remaining; requesting 8h emergency → blocked
        var ent = E.LeaveEntitlement.Create(staff.Id, today.Year, annualHours: 2m, sickHours: 14m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            today, today, null, null, 8m, null, "pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_Emergency_ThrowsWhenNoAnnualEntitlementSet()
    {
        // Emergency leave is blocked when no annual entitlement has been configured
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        // No entitlement seeded at all
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var today = Weekday(DateTime.UtcNow);
        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            today, today, null, null, 8m, null, "pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertLeave_Emergency_CanBeAppliedSameDay_NoAdvanceRequired()
    {
        // Emergency leave is not subject to the annual 3-day advance rule
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var ent = E.LeaveEntitlement.Create(staff.Id, now.Year, annualHours: 40m, sickHours: 14m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(now);
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Same day — would fail for annual but is fine for emergency
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
        var staff = await MakeStaff(db);
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

    // ── Item 1: Sick leave auto-approve ───────────────────────────────────────

    [Fact]
    public async Task UpsertLeave_Sick_IsAutoApproved_WhenSubmittedAsPending()
    {
        // Staff self-reports sick leave → it must be auto-approved, not left pending
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var today = Weekday(DateTime.UtcNow);
        var ent = E.LeaveEntitlement.Create(staff.Id, today.Year, annualHours: 40m, sickHours: 40m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(null, staff.Id, "sick",
            today, today, null, null, 8m, null, "pending");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);

        var leave = await db.LeaveRequests.FirstAsync(l => l.Id == leaveId);
        Assert.Equal("approved", leave.Status);
    }

    [Fact]
    public async Task UpsertLeave_Sick_NotifiesManagers_WhenAutoApproved()
    {
        // When sick leave is auto-approved, managers should receive a notification
        await using var db = CreateDb();
        var staff   = await MakeStaff(db, "S001");
        // Seed the MANAGER role row so NotifyManagersAsync (which filters on Role.Name == MANAGER via
        // the AutoInclude nav) can find this manager under the InMemory provider.
        var manager = await RoleSeed.AddStaffWithRolesAsync(db, "M001", "Bob", "Manager", "bob@test.com", "hash", "MANAGER");
        var today = Weekday(DateTime.UtcNow);
        var ent = E.LeaveEntitlement.Create(staff.Id, today.Year, annualHours: 40m, sickHours: 40m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(null, staff.Id, "sick",
            today, today, null, null, 8m, null, "pending");

        await handler.Handle(cmd, CancellationToken.None);

        // Manager should have received a sick-leave notification
        var mgNotif = await db.Notifications
            .FirstOrDefaultAsync(n => n.EmployeeId == manager.Id);
        Assert.NotNull(mgNotif);
        Assert.Contains("sick", mgNotif.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertLeave_Sick_AdminCanSetApprovedDirectly_WithoutAutoApproveLogic()
    {
        // Admin setting sick status to 'approved' directly — should not double-notify employee
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var today = Weekday(DateTime.UtcNow);
        var ent = E.LeaveEntitlement.Create(staff.Id, today.Year, annualHours: 40m, sickHours: 40m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        // Admin creates sick with status 'approved' directly (not pending)
        var cmd = new UpsertLeaveCommand(null, staff.Id, "sick",
            today, today, null, null, 8m, null, "approved");

        var leaveId = await handler.Handle(cmd, CancellationToken.None);

        var leave = await db.LeaveRequests.FirstAsync(l => l.Id == leaveId);
        Assert.Equal("approved", leave.Status);
        // No employee notification created (was not auto-approved from pending)
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    // ── Item 2: Manager notified for emergency leave ──────────────────────────

    [Fact]
    public async Task UpsertLeave_Emergency_NotifiesManagers_WhenAutoApproved()
    {
        // Emergency leave submitted as pending → auto-approve AND notify managers
        await using var db = CreateDb();
        var staff   = await MakeStaff(db, "S001");
        // Seed the MANAGER role row so NotifyManagersAsync (which filters on Role.Name == MANAGER via
        // the AutoInclude nav) can find this manager under the InMemory provider.
        var manager = await RoleSeed.AddStaffWithRolesAsync(db, "M001", "Carol", "Manager", "carol@test.com", "hash", "MANAGER");
        var today = Weekday(DateTime.UtcNow);
        var ent = E.LeaveEntitlement.Create(staff.Id, today.Year, annualHours: 40m, sickHours: 14m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(null, staff.Id, "emergency",
            today, today, null, null, 4m, null, "pending");

        await handler.Handle(cmd, CancellationToken.None);

        // Manager should have received an emergency-leave notification
        var mgNotif = await db.Notifications
            .FirstOrDefaultAsync(n => n.EmployeeId == manager.Id);
        Assert.NotNull(mgNotif);
        Assert.Contains("emergency", mgNotif.Title, StringComparison.OrdinalIgnoreCase);
    }

    // ── Item 8: GetLeaveBalanceQuery — emergencyUsed + combined annualUsed ────

    [Fact]
    public async Task GetLeaveBalance_IncludesEmergency_InAnnualUsed()
    {
        // Emergency hours must be counted against the annual pool in the balance query
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var year  = 2026;
        var jan1  = new DateTime(year, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var ent   = E.LeaveEntitlement.Create(staff.Id, year, annualHours: 40m, sickHours: 14m);
        var annualLeave = E.LeaveRequest.Create(staff.Id, "annual", jan1, jan1, null, null, 8m, null);
        annualLeave.Update("annual", jan1, jan1, null, null, 8m, null, "approved");
        var emergencyLeave = E.LeaveRequest.Create(staff.Id, "emergency", jan1.AddDays(1), jan1.AddDays(1), null, null, 4m, null);
        emergencyLeave.Update("emergency", jan1.AddDays(1), jan1.AddDays(1), null, null, 4m, null, "approved");

        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        db.LeaveRequests.Add(annualLeave);
        db.LeaveRequests.Add(emergencyLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveBalanceQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveBalanceQuery(staff.Id, year), CancellationToken.None);

        // annualUsed = 8h annual + 4h emergency = 12h
        Assert.Equal(12m, result.AnnualUsed);
        Assert.Equal(28m, result.AnnualRemaining);  // 40 - 12
        Assert.Equal(4m,  result.EmergencyUsed);    // only emergency hours
    }

    [Fact]
    public async Task GetLeaveBalance_EmergencyUsed_IsZero_WhenNoEmergencyLeave()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var year  = 2026;
        var jan1  = new DateTime(year, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var ent   = E.LeaveEntitlement.Create(staff.Id, year, annualHours: 40m, sickHours: 14m);
        var annualLeave = E.LeaveRequest.Create(staff.Id, "annual", jan1, jan1, null, null, 8m, null);
        annualLeave.Update("annual", jan1, jan1, null, null, 8m, null, "approved");

        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        db.LeaveRequests.Add(annualLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveBalanceQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveBalanceQuery(staff.Id, year), CancellationToken.None);

        Assert.Equal(8m, result.AnnualUsed);
        Assert.Equal(0m, result.EmergencyUsed);
    }

    // ── Item 5: BulkUpsertLeaveEntitlementCommand ─────────────────────────────

    [Fact]
    public async Task BulkUpsertLeaveEntitlement_ThrowsArgument_WhenNegativeHours()
    {
        await using var db = CreateDb();
        var handler = new BulkUpsertLeaveEntitlementCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new BulkUpsertLeaveEntitlementCommand(2026, -1m, 14m),
                CancellationToken.None));
    }

    [Fact]
    public async Task BulkUpsertLeaveEntitlement_ReturnsZero_WhenNoActiveStaff()
    {
        await using var db = CreateDb();
        var handler = new BulkUpsertLeaveEntitlementCommandHandler(db);

        var count = await handler.Handle(
            new BulkUpsertLeaveEntitlementCommand(2026, 112m, 56m), CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task BulkUpsertLeaveEntitlement_CreatesEntitlements_ForAllActiveStaff()
    {
        await using var db = CreateDb();
        var s1 = await MakeStaff(db, "S001");
        var bartender = await RoleSeed.RoleIdAsync(db, "BARTENDER");
        var s2 = E.Staff.Create("S002", "Bob", "Lee", "bob@test.com", "hash", new List<Guid> { bartender });
        db.Staff.Add(s1);
        db.Staff.Add(s2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new BulkUpsertLeaveEntitlementCommandHandler(db);
        var count = await handler.Handle(
            new BulkUpsertLeaveEntitlementCommand(2026, 112m, 56m), CancellationToken.None);

        Assert.Equal(2, count);
        var entitlements = await db.LeaveEntitlements.Where(e => e.Year == 2026).ToListAsync();
        Assert.Equal(2, entitlements.Count);
        Assert.All(entitlements, e =>
        {
            Assert.Equal(112m, e.AnnualHours);
            Assert.Equal(56m,  e.SickHours);
        });
    }

    [Fact]
    public async Task BulkUpsertLeaveEntitlement_UpdatesExistingEntitlements()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var existing = E.LeaveEntitlement.Create(staff.Id, 2026, 40m, 20m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(existing);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new BulkUpsertLeaveEntitlementCommandHandler(db);
        await handler.Handle(new BulkUpsertLeaveEntitlementCommand(2026, 112m, 56m),
            CancellationToken.None);

        var updated = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id && e.Year == 2026);
        Assert.Equal(112m, updated.AnnualHours);
        Assert.Equal(56m,  updated.SickHours);
    }

    [Fact]
    public async Task BulkUpsertLeaveEntitlement_IgnoresInactiveStaff()
    {
        await using var db = CreateDb();
        var active   = await MakeStaff(db, "S001");
        var bartender = await RoleSeed.RoleIdAsync(db, "BARTENDER");
        var inactive = E.Staff.Create("S002", "Jane", "Doe", "jane@test.com", "hash", new List<Guid> { bartender });
        inactive.Deactivate();
        db.Staff.Add(active);
        db.Staff.Add(inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new BulkUpsertLeaveEntitlementCommandHandler(db);
        var count = await handler.Handle(
            new BulkUpsertLeaveEntitlementCommand(2026, 112m, 56m), CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(1, await db.LeaveEntitlements.CountAsync());
    }

    // ── Item 6: CarryOverLeaveEntitlementCommand ──────────────────────────────

    [Fact]
    public async Task CarryOverLeaveEntitlement_ThrowsArgument_WhenFromYearNotBeforeToYear()
    {
        await using var db = CreateDb();
        var handler = new CarryOverLeaveEntitlementCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CarryOverLeaveEntitlementCommand(2026, 2025),
                CancellationToken.None));
    }

    [Fact]
    public async Task CarryOverLeaveEntitlement_ThrowsArgument_WhenMaxCarryOverNegative()
    {
        await using var db = CreateDb();
        var handler = new CarryOverLeaveEntitlementCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CarryOverLeaveEntitlementCommand(2025, 2026, -1m),
                CancellationToken.None));
    }

    [Fact]
    public async Task CarryOverLeaveEntitlement_CarriesOverUnusedLeave()
    {
        // Staff has 40h annual for 2025, used 32h (Mon-Thu) → 8h remaining → carry-over up to 14h = 8h
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var ent2025 = E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 40m, sickHours: 14m);
        var ent2026 = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m, sickHours: 14m);
        var usedLeave = E.LeaveRequest.Create(staff.Id, "annual",
            new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 6, 0, 0, 0, DateTimeKind.Utc),
            null, null, 32m, null);
        usedLeave.Update("annual",
            new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 6, 0, 0, 0, DateTimeKind.Utc),
            null, null, 32m, null, "approved");

        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent2025);
        db.LeaveEntitlements.Add(ent2026);
        db.LeaveRequests.Add(usedLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new CarryOverLeaveEntitlementCommandHandler(db);
        var count = await handler.Handle(
            new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);

        Assert.Equal(1, count);
        var updated2026 = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id && e.Year == 2026);
        Assert.Equal(88m, updated2026.AnnualHours);   // 80 base + 8 carry-over (remaining was 8)
    }

    [Fact]
    public async Task CarryOverLeaveEntitlement_IsIdempotent_WhenRunTwice()
    {
        // Re-running carry-over for the same year pair must NOT double-apply (AddCarryOver is +=).
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 40m, sickHours: 14m));
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m, sickHours: 14m));
        var used = E.LeaveRequest.Create(staff.Id, "annual",
            new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 6, 0, 0, 0, DateTimeKind.Utc), null, null, 32m, null);
        used.Update("annual",
            new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 6, 0, 0, 0, DateTimeKind.Utc), null, null, 32m, null, "approved");
        db.Staff.Add(staff);
        db.LeaveRequests.Add(used);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new CarryOverLeaveEntitlementCommandHandler(db);
        var first = await handler.Handle(new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);
        db.ChangeTracker.Clear();
        var second = await handler.Handle(new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);   // second run skips the already-carried employee
        var ent = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id && e.Year == 2026);
        Assert.Equal(88m, ent.AnnualHours);   // still 80 + 8, NOT 80 + 16
    }

    [Fact]
    public async Task CarryOverLeaveEntitlement_CapsAtMaxCarryOver()
    {
        // Staff has 40h for 2025, used 0h → 40h remaining → capped at 14h
        await using var db = CreateDb();
        var staff   = await MakeStaff(db);
        var ent2025 = E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 40m, sickHours: 14m);
        var ent2026 = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m, sickHours: 14m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent2025);
        db.LeaveEntitlements.Add(ent2026);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new CarryOverLeaveEntitlementCommandHandler(db);
        await handler.Handle(new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);

        var updated2026 = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id && e.Year == 2026);
        Assert.Equal(94m, updated2026.AnnualHours);   // 80 + 14 (capped)
    }

    [Fact]
    public async Task CarryOverLeaveEntitlement_CreatesNewEntitlement_WhenToYearHasNone()
    {
        // Staff has entitlement for 2025 but none for 2026 → creates new with carry-over only
        await using var db = CreateDb();
        var staff   = await MakeStaff(db);
        var ent2025 = E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 40m, sickHours: 14m);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent2025);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new CarryOverLeaveEntitlementCommandHandler(db);
        var count = await handler.Handle(
            new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);

        Assert.Equal(1, count);
        var new2026 = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id && e.Year == 2026);
        Assert.Equal(14m, new2026.AnnualHours);  // carry-over (all 40 → capped at 14)
        Assert.Equal(0m,  new2026.SickHours);
    }

    [Fact]
    public async Task CarryOverLeaveEntitlement_SkipsEmployee_WhenNoRemainingLeave()
    {
        // Staff used all their leave → nothing to carry over → returns 0
        await using var db = CreateDb();
        var staff   = await MakeStaff(db);
        var ent2025 = E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 40m, sickHours: 14m);
        var usedLeave = E.LeaveRequest.Create(staff.Id, "annual",
            new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            null, null, 40m, null);
        usedLeave.Update("annual",
            new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            null, null, 40m, null, "approved");
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent2025);
        db.LeaveRequests.Add(usedLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new CarryOverLeaveEntitlementCommandHandler(db);
        var count = await handler.Handle(
            new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, await db.LeaveEntitlements.CountAsync(e => e.Year == 2026));
    }

    // ── Item 1: RejectionReason stored / cleared via UpsertLeaveCommand ──────

    [Fact]
    public async Task UpsertLeave_Update_StoresRejectionReason_WhenStatusIsRejected()
    {
        // Manager rejects a pending request and provides a reason.
        // The reason must be persisted so staff can see why it was denied.
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var today = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var ent   = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 40m, sickHours: 14m);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", today, today, null, null, 8m, null);
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(
            leave.Id, staff.Id, "annual", today, today,
            null, null, 8m, null, "rejected", "Insufficient annual leave balance.");

        await handler.Handle(cmd, CancellationToken.None);

        var updated = await db.LeaveRequests.FindAsync(leave.Id);
        Assert.Equal("rejected",                         updated!.Status);
        Assert.Equal("Insufficient annual leave balance.", updated.RejectionReason);
    }

    [Fact]
    public async Task UpsertLeave_Update_RefusesToReapproveARejectedLeave_AndKeepsTheReason()
    {
        // Phase 7l (D6): rejected is final. Re-approving it would skip the balance and overlap
        // rules a fresh request goes through, so the staff member must apply again. The stored
        // rejection reason stays so the history still explains the decision.
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var today = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var ent   = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 40m, sickHours: 14m);
        var leave = E.LeaveRequest.Create(staff.Id, "annual", today, today, null, null, 8m, null);
        leave.Update("annual", today, today, null, null, 8m, null, "rejected", "Old reason");
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider();
        var handler = new UpsertLeaveCommandHandler(db, new NullNotificationHub(),
            new NullPushSvc(), new NullAuditService(), dt);

        var cmd = new UpsertLeaveCommand(
            leave.Id, staff.Id, "annual", today, today,
            null, null, 8m, null, "approved", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(cmd, CancellationToken.None));

        db.ChangeTracker.Clear();
        var updated = await db.LeaveRequests.FindAsync(leave.Id);
        Assert.Equal("rejected", updated!.Status);
        Assert.Equal("Old reason", updated.RejectionReason);
    }

    // ── Item 5: CarryOverHours field tracked by AddCarryOver() ───────────────

    [Fact]
    public async Task CarryOverLeaveEntitlement_SetsCarryOverHours_OnEntitlement()
    {
        // CarryOverHours on the target-year entitlement must reflect exactly
        // how much was carried over, so the balance screen can display it.
        await using var db = CreateDb();
        var staff   = await MakeStaff(db);
        var ent2025 = E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 40m, sickHours: 14m);
        var ent2026 = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m, sickHours: 14m);
        // Used 32h (Mon-Thu) → 8h remaining → carry-over = 8h (under the 14h cap)
        var usedLeave = E.LeaveRequest.Create(staff.Id, "annual",
            new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 6, 0, 0, 0, DateTimeKind.Utc),
            null, null, 32m, null);
        usedLeave.Update("annual",
            new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 6, 0, 0, 0, DateTimeKind.Utc),
            null, null, 32m, null, "approved");
        db.Staff.Add(staff);
        db.LeaveEntitlements.Add(ent2025);
        db.LeaveEntitlements.Add(ent2026);
        db.LeaveRequests.Add(usedLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new CarryOverLeaveEntitlementCommandHandler(db);
        await handler.Handle(new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);

        var updated2026 = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id && e.Year == 2026);
        Assert.Equal(8m, updated2026.CarryOverHours);  // tracked separately from AnnualHours delta
    }

    // ── Item 3: BulkUpdateLeaveStatusCommand ─────────────────────────────────

    [Fact]
    public async Task BulkUpdateLeaveStatus_ReturnsZero_WhenIdsListIsEmpty()
    {
        await using var db = CreateDb();
        var handler = new BulkUpdateLeaveStatusCommandHandler(
            db, new NullNotificationHub(), new NullPushSvc(), new NullAuditService());

        var count = await handler.Handle(
            new BulkUpdateLeaveStatusCommand([], "approved"), CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task BulkUpdateLeaveStatus_ThrowsArgument_WhenStatusIsNotApprovedOrRejected()
    {
        // Only approved/rejected are valid bulk targets — prevent managers from
        // bulk-cancelling records they don't own.
        await using var db = CreateDb();
        var handler = new BulkUpdateLeaveStatusCommandHandler(
            db, new NullNotificationHub(), new NullPushSvc(), new NullAuditService());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new BulkUpdateLeaveStatusCommand([Guid.NewGuid()], "cancelled"),
                CancellationToken.None));
    }

    [Fact]
    public async Task BulkUpdateLeaveStatus_ThrowsArgument_WhenMoreThan100Ids()
    {
        // Cap prevents accidentally approving an unbounded set in one API call.
        await using var db = CreateDb();
        var handler = new BulkUpdateLeaveStatusCommandHandler(
            db, new NullNotificationHub(), new NullPushSvc(), new NullAuditService());

        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new BulkUpdateLeaveStatusCommand(ids, "approved"),
                CancellationToken.None));
    }

    [Fact]
    public async Task BulkUpdateLeaveStatus_SkipsAlreadyResolvedRecords_OnlyUpdatesPending()
    {
        // Already-approved/rejected records must not be double-processed.
        await using var db = CreateDb();
        var staff   = await MakeStaff(db);
        var pending  = MakeLeave(staff.Id, "pending");
        var approved = MakeLeave(staff.Id, "approved");
        db.Staff.Add(staff);
        db.LeaveRequests.Add(pending);
        db.LeaveRequests.Add(approved);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new BulkUpdateLeaveStatusCommandHandler(
            db, new NullNotificationHub(), new NullPushSvc(), new NullAuditService());

        var count = await handler.Handle(
            new BulkUpdateLeaveStatusCommand([pending.Id, approved.Id], "approved"),
            CancellationToken.None);

        // Only the pending record should have been updated
        Assert.Equal(1, count);
        var stillApproved = await db.LeaveRequests.FindAsync(approved.Id);
        Assert.Equal("approved", stillApproved!.Status);  // unchanged
    }

    [Fact]
    public async Task BulkUpdateLeaveStatus_ApprovesPendingRecords_AndReturnsCount()
    {
        await using var db = CreateDb();
        var staff  = await MakeStaff(db);
        var leave1 = MakeLeave(staff.Id, "pending");
        var leave2 = MakeLeave(staff.Id, "pending");
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave1);
        db.LeaveRequests.Add(leave2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new BulkUpdateLeaveStatusCommandHandler(
            db, new NullNotificationHub(), new NullPushSvc(), new NullAuditService());

        var count = await handler.Handle(
            new BulkUpdateLeaveStatusCommand([leave1.Id, leave2.Id], "approved"),
            CancellationToken.None);

        Assert.Equal(2, count);
        var l1 = await db.LeaveRequests.FindAsync(leave1.Id);
        var l2 = await db.LeaveRequests.FindAsync(leave2.Id);
        Assert.Equal("approved", l1!.Status);
        Assert.Equal("approved", l2!.Status);
    }

    private sealed class ThrowingNotificationHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated dead WebSocket connection");
    }

    [Fact]
    public async Task BulkUpdateLeaveStatus_StillCommitsBatch_WhenLiveNotificationSendThrows()
    {
        // A flaky SignalR/push send must not abort the approval batch: the status changes and
        // the in-app Notification rows are persisted first, and live delivery is best-effort.
        await using var db = CreateDb();
        var staff  = await MakeStaff(db);
        var leave1 = MakeLeave(staff.Id, "pending");
        var leave2 = MakeLeave(staff.Id, "pending");
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave1);
        db.LeaveRequests.Add(leave2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new BulkUpdateLeaveStatusCommandHandler(
            db, new ThrowingNotificationHub(), new NullPushSvc(), new NullAuditService());

        var count = await handler.Handle(
            new BulkUpdateLeaveStatusCommand([leave1.Id, leave2.Id], "approved"),
            CancellationToken.None);

        Assert.Equal(2, count);
        var l1 = await db.LeaveRequests.FindAsync(leave1.Id);
        var l2 = await db.LeaveRequests.FindAsync(leave2.Id);
        Assert.Equal("approved", l1!.Status);
        Assert.Equal("approved", l2!.Status);
        // The durable in-app notifications are still written despite the live-send failure.
        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task BulkUpdateLeaveStatus_StoresRejectionReason_WhenRejectingWithReason()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db);
        var leave = MakeLeave(staff.Id, "pending");
        db.Staff.Add(staff);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new BulkUpdateLeaveStatusCommandHandler(
            db, new NullNotificationHub(), new NullPushSvc(), new NullAuditService());

        await handler.Handle(
            new BulkUpdateLeaveStatusCommand([leave.Id], "rejected", "No balance remaining"),
            CancellationToken.None);

        var updated = await db.LeaveRequests.FindAsync(leave.Id);
        Assert.Equal("rejected",              updated!.Status);
        Assert.Equal("No balance remaining",  updated.RejectionReason);
    }

    // ── Item 11: GetLeaveSummaryQuery ─────────────────────────────────────────

    [Fact]
    public async Task GetLeaveSummary_ReturnsEmpty_WhenNoActiveStaff()
    {
        await using var db = CreateDb();
        var dt = new StubDateTimeProvider(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveSummaryQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveSummaryQuery(2026), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLeaveSummary_GroupsByEmployee_SumsHoursByLeaveType()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db, "S001");
        var yr    = 2026;
        var jan   = new DateTime(yr, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var annualLeave = E.LeaveRequest.Create(staff.Id, "annual", jan, jan, null, null, 8m, null);
        annualLeave.Update("annual", jan, jan, null, null, 8m, null, "approved");
        var sickLeave = E.LeaveRequest.Create(staff.Id, "sick", jan.AddDays(10), jan.AddDays(10), null, null, 4m, null);
        sickLeave.Update("sick", jan.AddDays(10), jan.AddDays(10), null, null, 4m, null, "approved");

        db.Staff.Add(staff);
        db.LeaveRequests.Add(annualLeave);
        db.LeaveRequests.Add(sickLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(new DateTime(yr, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveSummaryQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveSummaryQuery(yr), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(8m,  result[0].AnnualUsed);
        Assert.Equal(4m,  result[0].SickUsed);
        Assert.Equal(12m, result[0].TotalUsed);
    }

    [Fact]
    public async Task GetLeaveSummary_ExcludesRejectedAndCancelledLeaves()
    {
        // Only approved/pending leave counts toward consumed leave hours in the summary.
        await using var db = CreateDb();
        var staff = await MakeStaff(db, "S001");
        var yr    = 2026;
        var jan   = new DateTime(yr, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var approvedLeave  = E.LeaveRequest.Create(staff.Id, "annual", jan, jan, null, null, 8m, null);
        approvedLeave.Update("annual", jan, jan, null, null, 8m, null, "approved");
        var rejectedLeave  = E.LeaveRequest.Create(staff.Id, "annual", jan.AddDays(5), jan.AddDays(5), null, null, 8m, null);
        rejectedLeave.Update("annual", jan.AddDays(5), jan.AddDays(5), null, null, 8m, null, "rejected");
        var cancelledLeave = E.LeaveRequest.Create(staff.Id, "annual", jan.AddDays(10), jan.AddDays(10), null, null, 8m, null);
        cancelledLeave.Update("annual", jan.AddDays(10), jan.AddDays(10), null, null, 8m, null, "cancelled");

        db.Staff.Add(staff);
        db.LeaveRequests.Add(approvedLeave);
        db.LeaveRequests.Add(rejectedLeave);
        db.LeaveRequests.Add(cancelledLeave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(new DateTime(yr, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetLeaveSummaryQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeaveSummaryQuery(yr), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(8m, result[0].AnnualUsed);  // only the approved one
        Assert.Equal(8m, result[0].TotalUsed);
    }

    // ── Item 12: GetLeavePatternWarningsQuery ─────────────────────────────────

    [Fact]
    public async Task GetLeavePatternWarnings_ReturnsEmpty_WhenNoFrequentSickLeave()
    {
        // Staff with fewer than 3 sick leaves in 90 days must not be flagged.
        await using var db = CreateDb();
        var staff  = await MakeStaff(db, "S001");
        var today  = new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc);
        var recent = today.AddDays(-10);

        var sick1 = E.LeaveRequest.Create(staff.Id, "sick", recent, recent, null, null, 8m, null);
        sick1.Update("sick", recent, recent, null, null, 8m, null, "approved");
        var sick2 = E.LeaveRequest.Create(staff.Id, "sick", recent.AddDays(-5), recent.AddDays(-5), null, null, 8m, null);
        sick2.Update("sick", recent.AddDays(-5), recent.AddDays(-5), null, null, 8m, null, "approved");

        db.Staff.Add(staff);
        db.LeaveRequests.Add(sick1);
        db.LeaveRequests.Add(sick2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(today);
        var handler = new GetLeavePatternWarningsQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeavePatternWarningsQuery(), CancellationToken.None);

        // 2 sick leaves < threshold of 3 → no warning
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLeavePatternWarnings_FlagsEmployees_With3OrMoreSickLeavesInWindow()
    {
        await using var db = CreateDb();
        var staff = await MakeStaff(db, "S001");
        var today = new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc);

        // 3 sick leaves all within the 90-day window
        for (var i = 0; i < 3; i++)
        {
            var d = today.AddDays(-(i * 10));
            var sick = E.LeaveRequest.Create(staff.Id, "sick", d, d, null, null, 8m, null);
            sick.Update("sick", d, d, null, null, 8m, null, "approved");
            db.LeaveRequests.Add(sick);
        }

        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(today);
        var handler = new GetLeavePatternWarningsQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeavePatternWarningsQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(staff.Id, result[0].EmployeeId);
        Assert.Equal(3,        result[0].SickLeaveCount);
        Assert.Equal(24m,      result[0].SickLeaveHours);
    }

    [Fact]
    public async Task GetLeavePatternWarnings_DoesNotFlagSickLeaves_OutsideThe90DayWindow()
    {
        // A sick-leave history older than 90 days must not trigger a warning today.
        await using var db = CreateDb();
        var staff = await MakeStaff(db, "S001");
        var today = new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc);

        // 3 sick leaves but all older than 90 days
        for (var i = 0; i < 3; i++)
        {
            var d = today.AddDays(-(100 + i));
            var sick = E.LeaveRequest.Create(staff.Id, "sick", d, d, null, null, 8m, null);
            sick.Update("sick", d, d, null, null, 8m, null, "approved");
            db.LeaveRequests.Add(sick);
        }

        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dt = new StubDateTimeProvider(today);
        var handler = new GetLeavePatternWarningsQueryHandler(db, dt);

        var result = await handler.Handle(new GetLeavePatternWarningsQuery(), CancellationToken.None);

        // All 3 are outside the 90-day window → no warning
        Assert.Empty(result);
    }
}
