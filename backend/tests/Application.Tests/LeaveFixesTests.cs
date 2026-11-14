using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7l (PR A) leave correctness rules: cancelled leave never blocks a re-apply (D1), nobody
/// approves or rejects their own leave (D3), status changes follow a state machine (D6), and
/// managers are told when approved leave is withdrawn (D7).
/// </summary>
public class LeaveFixesTests
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

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => DateTime.UtcNow;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<E.Staff> AddStaff(TestDbContext db, string id, string role = "BARTENDER")
    {
        var roleId = await RoleSeed.EnsureRoleAsync(db, role);
        var staff = E.Staff.Create(id, "Test", id, $"{id.ToLower()}@t.com", "hash", new List<Guid> { roleId });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    /// <summary>A weekday <paramref name="offset"/> days from today (leave needs a working day).</summary>
    private static DateTime Day(int offset)
    {
        var d = DateTime.UtcNow.Date.AddDays(offset);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    private static async Task<E.LeaveRequest> AddLeave(TestDbContext db, Guid employeeId, string status,
        DateTime start, DateTime end, string type = "unpaid")
    {
        var leave = E.LeaveRequest.Create(employeeId, type, start, end, null, null, 8m, null);
        if (status != AppConstants.Leave.Pending) leave.Update(type, start, end, null, null, 8m, null, status);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return leave;
    }

    private static UpsertLeaveCommandHandler UpsertHandler(TestDbContext db) =>
        new(db, new NullHub(), new NullPush(), new NullAudit(), new Clock());

    private static UpsertLeaveCommand Cmd(Guid? id, Guid employeeId, string status, DateTime start, DateTime end,
        Guid? actingUserId = null) =>
        new(id, employeeId, "unpaid", start, end, null, null, 8m, null, status, ActingUserId: actingUserId);

    // ── D1: a cancelled leave must not block re-applying ──────────────────────

    [Fact]
    public async Task Apply_Succeeds_WhenTheOnlyOverlappingLeaveWasCancelled()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await AddLeave(db, staff.Id, AppConstants.Leave.Cancelled, Day(10), Day(11));

        var id = await UpsertHandler(db).Handle(Cmd(null, staff.Id, "pending", Day(10), Day(11)), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("approved")]
    public async Task Apply_StillThrows_WhenAnActiveLeaveOverlaps(string existingStatus)
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await AddLeave(db, staff.Id, existingStatus, Day(10), Day(11));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpsertHandler(db).Handle(Cmd(null, staff.Id, "pending", Day(11), Day(12)), CancellationToken.None));
    }

    // ── D3: no self-approval ──────────────────────────────────────────────────

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    public async Task Update_Throws_WhenApproverDecidesTheirOwnLeave(string decision)
    {
        await using var db = CreateDb();
        var manager = await AddStaff(db, "M1", "FRONT DESK");
        var leave = await AddLeave(db, manager.Id, AppConstants.Leave.Pending, Day(10), Day(10));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpsertHandler(db).Handle(Cmd(leave.Id, manager.Id, decision, Day(10), Day(10), actingUserId: manager.Id), CancellationToken.None));

        Assert.Contains("own leave", ex.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();
        Assert.Equal(AppConstants.Leave.Pending, (await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id)).Status);
    }

    [Fact]
    public async Task Create_Throws_WhenApproverRecordsTheirOwnLeaveAlreadyApproved()
    {
        await using var db = CreateDb();
        var manager = await AddStaff(db, "M1", "FRONT DESK");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpsertHandler(db).Handle(Cmd(null, manager.Id, "approved", Day(10), Day(10), actingUserId: manager.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Create_Succeeds_WhenApproverRecordsTheirOwnLeaveAsPending()
    {
        await using var db = CreateDb();
        var manager = await AddStaff(db, "M1", "FRONT DESK");

        var id = await UpsertHandler(db).Handle(Cmd(null, manager.Id, "pending", Day(10), Day(10), actingUserId: manager.Id), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Update_Succeeds_WhenADifferentApproverDecides()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var manager = await AddStaff(db, "M1", "FRONT DESK");
        var leave = await AddLeave(db, staff.Id, AppConstants.Leave.Pending, Day(10), Day(10));

        await UpsertHandler(db).Handle(Cmd(leave.Id, staff.Id, "approved", Day(10), Day(10), actingUserId: manager.Id), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(AppConstants.Leave.Approved, (await db.LeaveRequests.FirstAsync(l => l.Id == leave.Id)).Status);
    }

    [Fact]
    public async Task BulkApprove_Throws_AndChangesNothing_WhenSelectionContainsTheApproversOwnLeave()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var manager = await AddStaff(db, "M1", "FRONT DESK");
        var theirs = await AddLeave(db, staff.Id, AppConstants.Leave.Pending, Day(10), Day(10));
        var mine = await AddLeave(db, manager.Id, AppConstants.Leave.Pending, Day(12), Day(12));
        var handler = new BulkUpdateLeaveStatusCommandHandler(db, new NullHub(), new NullPush(), new NullAudit());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new BulkUpdateLeaveStatusCommand([theirs.Id, mine.Id], "approved", ActingUserId: manager.Id), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.All(await db.LeaveRequests.ToListAsync(), l => Assert.Equal(AppConstants.Leave.Pending, l.Status));
    }

    // ── D6: status state machine ──────────────────────────────────────────────

    [Theory]
    [InlineData("pending", "approved")]
    [InlineData("pending", "rejected")]
    [InlineData("pending", "cancelled")]
    [InlineData("approved", "rejected")]
    [InlineData("approved", "cancelled")]
    [InlineData("pending", "pending")]
    [InlineData("approved", "approved")]
    public void Update_AllowsLegalTransitions(string from, string to)
    {
        var leave = Leave(from);

        leave.Update("unpaid", Day(10), Day(10), null, null, 8m, null, to);

        Assert.Equal(to, leave.Status);
    }

    [Theory]
    [InlineData("cancelled", "approved")]
    [InlineData("cancelled", "pending")]
    [InlineData("rejected", "approved")]
    [InlineData("rejected", "pending")]
    [InlineData("approved", "pending")]
    public void Update_Throws_OnIllegalTransitions(string from, string to)
    {
        var leave = Leave(from);

        Assert.Throws<InvalidOperationException>(() =>
            leave.Update("unpaid", Day(10), Day(10), null, null, 8m, null, to));
        Assert.Equal(from, leave.Status);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    public void Cancel_Throws_WhenLeaveIsAlreadyClosed(string status)
    {
        Assert.Throws<InvalidOperationException>(() => Leave(status).Cancel());
    }

    [Fact]
    public async Task AdminEdit_CannotReviveACancelledLeave()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var manager = await AddStaff(db, "M1", "FRONT DESK");
        var leave = await AddLeave(db, staff.Id, AppConstants.Leave.Cancelled, Day(10), Day(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpsertHandler(db).Handle(Cmd(leave.Id, staff.Id, "approved", Day(10), Day(10), actingUserId: manager.Id), CancellationToken.None));
    }

    private static E.LeaveRequest Leave(string status)
    {
        var leave = E.LeaveRequest.Create(Guid.NewGuid(), "unpaid", Day(10), Day(10), null, null, 8m, null);
        // Reach the starting status through legal moves only: pending, then (approved|rejected|cancelled).
        if (status != AppConstants.Leave.Pending) leave.Update("unpaid", Day(10), Day(10), null, null, 8m, null, status);
        return leave;
    }

    // ── D7: withdrawing approved leave notifies managers ──────────────────────

    [Fact]
    public async Task CancelApprovedLeave_NotifiesEveryActiveLeaveApprover()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var manager = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        var leave = await AddLeave(db, staff.Id, AppConstants.Leave.Approved, Day(10), Day(11));
        var handler = new CancelLeaveCommandHandler(db, new Clock(), new NullHub());

        await handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None);

        var notes = await db.Notifications.Where(n => n.EmployeeId == manager.Id).ToListAsync();
        var note = Assert.Single(notes);
        Assert.Equal(AppConstants.NotificationType.LeavePending, note.Type);
        Assert.Contains("withdrawn", note.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelPendingLeave_DoesNotNotifyApprovers()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var manager = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        var leave = await AddLeave(db, staff.Id, AppConstants.Leave.Pending, Day(10), Day(11));
        var handler = new CancelLeaveCommandHandler(db, new Clock(), new NullHub());

        await handler.Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None);

        Assert.Empty(await db.Notifications.Where(n => n.EmployeeId == manager.Id).ToListAsync());
    }

    // ── Phase 7o: leave notices go to the people who can ACT on them (leave.approve), not to a role NAME ──

    [Fact]
    public async Task WithdrawingApprovedLeave_DoesNotNotifyARoleWithoutLeaveApprove()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var named = await AddStaff(db, "M1", "FRONT DESK");          // no leave.approve
        var leave = await AddLeave(db, staff.Id, AppConstants.Leave.Approved, Day(10), Day(11));

        await new CancelLeaveCommandHandler(db, new Clock(), new NullHub())
            .Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None);

        Assert.Empty(await db.Notifications.Where(n => n.EmployeeId == named.Id).ToListAsync());
    }

    [Fact]
    public async Task WithdrawingApprovedLeave_DoesNotNotifyTheWithdrawerEvenIfTheyCanApproveLeave()
    {
        await using var db = CreateDb();
        var approver = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        var leave = await AddLeave(db, approver.Id, AppConstants.Leave.Approved, Day(10), Day(11));

        await new CancelLeaveCommandHandler(db, new Clock(), new NullHub())
            .Handle(new CancelLeaveCommand(leave.Id, approver.Id), CancellationToken.None);

        Assert.Empty(await db.Notifications.Where(n => n.EmployeeId == approver.Id).ToListAsync());
    }

    [Fact]
    public async Task PendingLeave_NotifiesEveryLeaveApprover_AndNotARoleWithoutThePermission()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var approver = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        var named = await AddStaff(db, "M2", "FRONT DESK");           // no leave.approve

        await UpsertHandler(db).Handle(Cmd(null, staff.Id, "pending", Day(10), Day(11)), CancellationToken.None);

        var note = Assert.Single(await db.Notifications.Where(n => n.EmployeeId == approver.Id).ToListAsync());
        Assert.Equal(AppConstants.NotificationType.LeavePending, note.Type);
        Assert.Empty(await db.Notifications.Where(n => n.EmployeeId == named.Id).ToListAsync());
    }

    [Fact]
    public async Task PendingLeave_DoesNotNotifyTheApplicantWhoCanApproveLeave()
    {
        await using var db = CreateDb();
        var applicant = await AddStaff(db, "A1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        var other = await AddStaff(db, "M2", "ROTA LEAD");

        await UpsertHandler(db).Handle(Cmd(null, applicant.Id, "pending", Day(10), Day(11)), CancellationToken.None);

        Assert.Empty(await db.Notifications.Where(n => n.EmployeeId == applicant.Id).ToListAsync());
        Assert.Single(await db.Notifications.Where(n => n.EmployeeId == other.Id).ToListAsync());
    }

    [Fact]
    public async Task PendingLeaveRecordedByAnApprover_DoesNotNotifyThatApprover()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var recorder = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        var other = await AddStaff(db, "M2", "ROTA LEAD");

        await UpsertHandler(db).Handle(Cmd(null, staff.Id, "pending", Day(10), Day(11), actingUserId: recorder.Id), CancellationToken.None);

        Assert.Empty(await db.Notifications.Where(n => n.EmployeeId == recorder.Id).ToListAsync());
        Assert.Single(await db.Notifications.Where(n => n.EmployeeId == other.Id).ToListAsync());
    }

    [Fact]
    public async Task APerUserRevokeOfLeaveApprove_StopsTheNotice()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var revoked = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        db.StaffPermissionOverrides.Add(E.StaffPermissionOverride.Create(revoked.Id, AppConstants.Permissions.LeaveApprove, granted: false));
        await db.SaveChangesAsync();

        await UpsertHandler(db).Handle(Cmd(null, staff.Id, "pending", Day(10), Day(11)), CancellationToken.None);

        Assert.Empty(await db.Notifications.Where(n => n.EmployeeId == revoked.Id).ToListAsync());
    }
}
