using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7l review findings: an update is checked against the employee who OWNS the leave, closed
/// leave is immutable, past leave only changes status (or becomes a no-show), status-only edits never
/// re-run the hours rule, and nobody can mark their own leave a no-show.
/// The clock is fixed at Fri 13 Nov 2026 so "past" and "future" never depend on when the tests run.
/// </summary>
public class LeaveReviewFixesTests
{
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

    private sealed class Clock : IDateTimeProvider
    {
        private static readonly DateTime Now = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(Now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => Now;
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<E.Staff> AddStaff(TestDbContext db, string id, string role = "BARTENDER")
    {
        var roleId = await RoleSeed.EnsureRoleAsync(db, role);
        var staff = E.Staff.Create(id, "Test", id, $"{id.ToLower()}@t.com", "hash", new List<Guid> { roleId });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    private static async Task<E.LeaveRequest> AddLeave(TestDbContext db, Guid employeeId, string type, string status,
        DateTime start, DateTime end, decimal hours)
    {
        var leave = E.LeaveRequest.Create(employeeId, type, start, end, null, null, hours, null);
        if (status != AppConstants.Leave.Pending) leave.Update(type, start, end, null, null, hours, null, status);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return leave;
    }

    private static UpsertLeaveCommandHandler Handler(TestDbContext db) =>
        new(db, new NullHub(), new NullPush(), new NullAudit(), new Clock());

    private static UpsertLeaveCommand Edit(E.LeaveRequest leave, Guid employeeId, string type, string status,
        DateTime? start = null, DateTime? end = null, Guid? acting = null) =>
        new(leave.Id, employeeId, type, start ?? leave.StartDate, end ?? leave.EndDate, null, null, null, null, status, ActingUserId: acting);

    // ── Finding 1: the update is checked against the leave's real owner ───────

    [Fact]
    public async Task Update_NamingADifferentEmployee_IsRefused_SoTheOwnerCanNeverBeOverdrawn()
    {
        await using var db = CreateDb();
        var owner = await AddStaff(db, "A1");
        var other = await AddStaff(db, "B1");
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(other.Id, 2026, annualHours: 200m));   // B has plenty
        await db.SaveChangesAsync();
        var leave = await AddLeave(db, owner.Id, "annual", AppConstants.Leave.Pending, D(2026, 12, 7), D(2026, 12, 7), 8m);

        // Mon-Fri (40h) sent for A's leave but with B's employeeId, so B's free balance would pass the check.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Edit(leave, other.Id, "annual", "approved", D(2026, 12, 7), D(2026, 12, 11)), CancellationToken.None));

        db.ChangeTracker.Clear();
        var saved = await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id);
        Assert.Equal(8m, saved.Hours);
        Assert.Equal(AppConstants.Leave.Pending, saved.Status);
    }

    // ── Finding 2: closed leave is immutable; past leave only changes status ──

    [Theory]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    public void ClosedLeave_CannotBeEdited_NotEvenWithTheSameStatus(string status)
    {
        var leave = E.LeaveRequest.Create(Guid.NewGuid(), "unpaid", D(2026, 12, 7), D(2026, 12, 7), null, null, 8m, null);
        leave.Update("unpaid", D(2026, 12, 7), D(2026, 12, 7), null, null, 8m, null, status);

        Assert.Throws<InvalidOperationException>(() =>
            leave.Update("unpaid", D(2026, 12, 8), D(2026, 12, 8), null, null, 8m, "new dates", status));
    }

    [Fact]
    public async Task PastApprovedLeave_CannotBeRetypedToRefundTheBalance()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var admin = await AddStaff(db, "M1", "FRONT DESK");
        var leave = await AddLeave(db, staff.Id, "annual", AppConstants.Leave.Approved, D(2026, 6, 1), D(2026, 6, 1), 8m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(Edit(leave, staff.Id, "unpaid", "approved", acting: admin.Id), CancellationToken.None));
    }

    [Fact]
    public async Task PastLeave_CannotHaveItsDatesChanged()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var admin = await AddStaff(db, "M1", "FRONT DESK");
        var leave = await AddLeave(db, staff.Id, "unpaid", AppConstants.Leave.Approved, D(2026, 6, 1), D(2026, 6, 1), 8m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(Edit(leave, staff.Id, "unpaid", "approved", D(2026, 6, 2), D(2026, 6, 2), acting: admin.Id), CancellationToken.None));
    }

    [Fact]
    public async Task PastPendingLeave_CanStillBeRejected_AndMarkedNoShow()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var admin = await AddStaff(db, "M1", "FRONT DESK");
        var toReject = await AddLeave(db, staff.Id, "unpaid", AppConstants.Leave.Pending, D(2026, 6, 1), D(2026, 6, 1), 8m);
        var toNoShow = await AddLeave(db, staff.Id, "unpaid", AppConstants.Leave.Pending, D(2026, 6, 2), D(2026, 6, 2), 8m);

        await Handler(db).Handle(Edit(toReject, staff.Id, "unpaid", "rejected", acting: admin.Id), CancellationToken.None);
        await Handler(db).Handle(Edit(toNoShow, staff.Id, "no-show", "approved", acting: admin.Id), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(AppConstants.Leave.Rejected, (await db.LeaveRequests.FirstAsync(l => l.Id == toReject.Id)).Status);
        var noShow = await db.LeaveRequests.FirstAsync(l => l.Id == toNoShow.Id);
        Assert.Equal("no-show", noShow.LeaveType);
        Assert.Equal(AppConstants.Leave.Approved, noShow.Status);
    }

    // ── Finding 3: a status-only edit never re-runs the hours rule ────────────

    [Fact]
    public async Task ARowThatIsWeekendOnly_CanStillBeRejected_KeepingItsStoredHours()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var admin = await AddStaff(db, "M1", "FRONT DESK");
        // Saturday and Sunday: recorded before the working-day rule existed.
        var leave = await AddLeave(db, staff.Id, "unpaid", AppConstants.Leave.Pending, D(2026, 12, 12), D(2026, 12, 13), 16m);

        await Handler(db).Handle(Edit(leave, staff.Id, "unpaid", "rejected", acting: admin.Id), CancellationToken.None);

        db.ChangeTracker.Clear();
        var saved = await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id);
        Assert.Equal(AppConstants.Leave.Rejected, saved.Status);
        Assert.Equal(16m, saved.Hours);
    }

    // ── Finding 5: no-show is not a way around the self-approval rule ─────────

    [Fact]
    public async Task BulkNoShow_Throws_WhenTheSelectionContainsTheActorsOwnLeave()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var manager = await AddStaff(db, "M1", "FRONT DESK");
        var theirs = await AddLeave(db, staff.Id, "annual", AppConstants.Leave.Approved, D(2026, 12, 7), D(2026, 12, 7), 8m);
        var mine = await AddLeave(db, manager.Id, "annual", AppConstants.Leave.Approved, D(2026, 12, 8), D(2026, 12, 8), 8m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BulkMarkLeaveNoShowCommandHandler(db, new NullAudit())
                .Handle(new BulkMarkLeaveNoShowCommand([theirs.Id, mine.Id], ActingUserId: manager.Id), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.All(await db.LeaveRequests.ToListAsync(), l => Assert.Equal("annual", l.LeaveType));
    }

    [Fact]
    public async Task BulkNoShow_SkipsClosedLeave()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var open = await AddLeave(db, staff.Id, "annual", AppConstants.Leave.Approved, D(2026, 12, 7), D(2026, 12, 7), 8m);
        var cancelled = await AddLeave(db, staff.Id, "annual", AppConstants.Leave.Cancelled, D(2026, 12, 8), D(2026, 12, 8), 8m);

        var count = await new BulkMarkLeaveNoShowCommandHandler(db, new NullAudit())
            .Handle(new BulkMarkLeaveNoShowCommand([open.Id, cancelled.Id]), CancellationToken.None);

        Assert.Equal(1, count);
        db.ChangeTracker.Clear();
        Assert.Equal("annual", (await db.LeaveRequests.FirstAsync(l => l.Id == cancelled.Id)).LeaveType);
    }
}
