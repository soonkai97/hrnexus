using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Leave;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// What each leave type DOES once the rules come from the catalogue (Phase 7q): the owner's new types
/// (hospitalisation, maternity, paternity, compassionate) and every flag a type can carry.
///
/// WHY these tests matter: a type is company data, so each flag must change behaviour for ANY type, not
/// just the built-in names. Each test names the rule it protects: a 98-day maternity must not be blocked by
/// the 30-day cap, calendar days must read the same hours in every view, a sensitive type must never be
/// named in a message other people see, and a deactivated type must stop taking new requests without
/// stranding its old leave.
/// </summary>
public class LeaveTypesBehaviourTests
{
    // 2 Nov 2026 is a Monday.
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => Now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(Now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => Now;
    }

    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Records every push body, so a test can read what would reach a lock screen.</summary>
    private sealed class RecordingPush : IPushNotificationService
    {
        public List<string> Bodies { get; } = [];
        public Task SendAsync(Guid id, string t, string b, string? u = null, CancellationToken ct = default) { Bodies.Add($"{t}|{b}"); return Task.CompletedTask; }
        public Task SendToManyAsync(IEnumerable<Guid> ids, string t, string b, string? u = null, CancellationToken ct = default) { Bodies.Add($"{t}|{b}"); return Task.CompletedTask; }
    }

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

    private static UpsertLeaveCommandHandler Handler(TestDbContext db, RecordingPush? push = null) =>
        new(db, new NullHub(), push ?? new RecordingPush(), new NullAudit(), new Clock());

    private static UpsertLeaveCommand Apply(Guid employeeId, string type, DateTime start, DateTime end,
        string status = "pending", Guid? id = null, Guid? acting = null, string? startTime = null, string? endTime = null) =>
        new(id, employeeId, type, start, end, startTime, endTime, null, null, status, ActingUserId: acting);

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Saves a catalogue (the defaults, with the given changes) so a test can vary a type.</summary>
    private static async Task SaveCatalogue(TestDbContext db, Func<LeaveTypeSpec, LeaveTypeSpec>? change = null, string? only = null)
    {
        var all = LeaveTypeDefaults.All().ToList();
        if (change is not null)
            foreach (var t in all.Where(t => only is null || t.Key == only))
                t.Update(change(new LeaveTypeSpec(t.Key, t.Label, t.Colour, t.SortOrder, t.Active, t.Pool, t.AllowanceDays,
                    t.MaxDaysPerRequest, t.AutoApprove, t.AdvanceNoticeDays, t.AdvanceNoticeFromPolicy, t.DeductsPay,
                    t.AdminOnly, t.CountsCalendarDays, t.Sensitive)), hasLeave: false, "test");
        db.LeaveTypes.AddRange(all);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<decimal> StoredHours(TestDbContext db, Guid id) =>
        (await db.LeaveRequests.AsNoTracking().SingleAsync(l => l.Id == id)).Hours ?? 0m;

    // ── maternity and paternity: calendar days, own limit, no yearly balance ──────

    [Fact]
    public async Task Maternity_of_98_calendar_days_is_accepted_and_is_not_blocked_by_the_30_day_policy_cap()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");

        // Mon 14 Dec 2026 + 97 days = Sun 21 Mar 2027: 98 calendar days across weekends and New Year.
        var id = await Handler(db).Handle(Apply(staff.Id, "maternity", D(2026, 12, 14), D(2027, 3, 21)), CancellationToken.None);

        Assert.Equal(784m, await StoredHours(db, id));   // 98 days x 8h, weekends included
    }

    [Fact]
    public async Task Maternity_of_99_days_is_refused_by_the_types_own_limit()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Apply(staff.Id, "maternity", D(2026, 12, 14), D(2027, 3, 22)), CancellationToken.None));

        Assert.Contains("98", ex.Message);
    }

    [Fact]
    public async Task Paternity_allows_7_calendar_days_and_refuses_8()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");

        // Sat 5 Dec .. Fri 11 Dec: 7 calendar days, only 5 of them weekdays.
        var id = await Handler(db).Handle(Apply(staff.Id, "paternity", D(2026, 12, 5), D(2026, 12, 11)), CancellationToken.None);
        Assert.Equal(56m, await StoredHours(db, id));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Apply(staff.Id, "paternity", D(2027, 1, 5), D(2027, 1, 12)), CancellationToken.None));
    }

    [Fact]
    public async Task A_new_type_never_approves_itself_a_manager_decides_long_absences()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");

        var id = await Handler(db).Handle(Apply(staff.Id, "maternity", D(2026, 12, 14), D(2027, 3, 21)), CancellationToken.None);

        Assert.Equal(AppConstants.Leave.Pending, (await db.LeaveRequests.AsNoTracking().SingleAsync(l => l.Id == id)).Status);
    }

    [Fact]
    public async Task Compassionate_leave_is_limited_to_3_days_per_request_with_no_yearly_balance()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");

        // No entitlement row exists for this person, yet it needs none: it has no balance.
        await Handler(db).Handle(Apply(staff.Id, "compassionate", D(2026, 11, 9), D(2026, 11, 11)), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Apply(staff.Id, "compassionate", D(2026, 11, 16), D(2026, 11, 19)), CancellationToken.None));
    }

    // ── hospitalisation: its own company-wide allowance, per year ────────────────

    [Fact]
    public async Task Hospitalisation_draws_on_its_own_allowance_and_refuses_what_would_overdraw_it()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await SaveCatalogue(db, s => s with { AllowanceDays = 3m }, only: "hospitalisation");
        var h = Handler(db);

        await h.Handle(Apply(staff.Id, "hospitalisation", D(2026, 11, 9), D(2026, 11, 10)), CancellationToken.None);     // 2 of 3 days

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Handle(Apply(staff.Id, "hospitalisation", D(2026, 11, 16), D(2026, 11, 17)), CancellationToken.None));
        Assert.Contains("8h remaining", ex.Message);
        // One more day still fits.
        await h.Handle(Apply(staff.Id, "hospitalisation", D(2026, 11, 16), D(2026, 11, 16)), CancellationToken.None);
    }

    [Fact]
    public async Task Hospitalisation_is_charged_to_each_year_by_the_days_that_fall_in_it()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await SaveCatalogue(db, s => s with { AllowanceDays = 3m }, only: "hospitalisation");
        var h = Handler(db);

        // Thu 31 Dec 2026 + Fri 1 Jan 2027: one day in each year.
        await h.Handle(Apply(staff.Id, "hospitalisation", D(2026, 12, 31), D(2027, 1, 1)), CancellationToken.None);
        // 2027 now has 1 of 3 days used: two more fit, a third does not.
        await h.Handle(Apply(staff.Id, "hospitalisation", D(2027, 1, 4), D(2027, 1, 5)), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Handle(Apply(staff.Id, "hospitalisation", D(2027, 1, 6), D(2027, 1, 6)), CancellationToken.None));
    }

    [Fact]
    public async Task Hospitalisation_does_not_touch_the_sick_or_annual_balance()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 40m, sickHours: 16m));
        await db.SaveChangesAsync();
        var h = Handler(db);

        await h.Handle(Apply(staff.Id, "hospitalisation", D(2026, 11, 9), D(2026, 11, 13)), CancellationToken.None);   // 5 days

        var b = await new GetLeaveBalanceQueryHandler(db, new Clock()).Handle(new GetLeaveBalanceQuery(staff.Id, 2026), CancellationToken.None);
        Assert.Equal(0m, b.SickUsed);
        Assert.Equal(0m, b.AnnualUsed);
        var line = b.Types!.Single(t => t.Key == "hospitalisation");
        Assert.Equal(40m, line.Used);
        Assert.Equal(480m, line.Entitlement);   // 60 days x 8h
        Assert.Equal(440m, line.Remaining);
    }

    // ── flags ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AutoApprove_follows_the_flag_not_the_name()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 40m, sickHours: 40m));
        await db.SaveChangesAsync();
        // Emergency stops approving itself; sick keeps doing so.
        await SaveCatalogue(db, s => s with { AutoApprove = false }, only: "emergency");
        var h = Handler(db);

        var emergency = await h.Handle(Apply(staff.Id, "emergency", D(2026, 11, 9), D(2026, 11, 9)), CancellationToken.None);
        var sick = await h.Handle(Apply(staff.Id, "sick", D(2026, 11, 10), D(2026, 11, 10)), CancellationToken.None);

        Assert.Equal("pending", (await db.LeaveRequests.AsNoTracking().SingleAsync(l => l.Id == emergency)).Status);
        Assert.Equal("approved", (await db.LeaveRequests.AsNoTracking().SingleAsync(l => l.Id == sick)).Status);
    }

    [Fact]
    public async Task Notice_comes_from_the_types_own_days_when_it_has_them()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await SaveCatalogue(db, s => s with { AdvanceNoticeDays = 10 }, only: "compassionate");
        var h = Handler(db);

        // Today is Mon 2 Nov: 10 days of notice means 12 Nov or later.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Handle(Apply(staff.Id, "compassionate", D(2026, 11, 11), D(2026, 11, 11)), CancellationToken.None));
        await h.Handle(Apply(staff.Id, "compassionate", D(2026, 11, 12), D(2026, 11, 12)), CancellationToken.None);
    }

    [Fact]
    public async Task A_types_day_limit_can_be_stricter_than_the_policy_cap()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await SaveCatalogue(db, s => s with { MaxDaysPerRequest = 5m }, only: "unpaid");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Apply(staff.Id, "unpaid", D(2026, 11, 9), D(2026, 11, 16)), CancellationToken.None));
    }

    // ── active and inactive types ────────────────────────────────────────────────

    [Fact]
    public async Task A_deactivated_type_takes_no_new_requests()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await SaveCatalogue(db, s => s with { Active = false }, only: "paternity");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Apply(staff.Id, "paternity", D(2026, 12, 5), D(2026, 12, 11)), CancellationToken.None));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task Leave_of_a_deactivated_type_can_still_be_approved_and_cancelled()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var approver = await AddStaff(db, "M1");
        var id = await Handler(db).Handle(Apply(staff.Id, "paternity", D(2026, 12, 5), D(2026, 12, 11)), CancellationToken.None);
        // The type is switched off while the request is still waiting.
        db.LeaveTypes.AddRange(LeaveTypeDefaults.All());
        await db.SaveChangesAsync();
        var p = await db.LeaveTypes.SingleAsync(t => t.Key == "paternity");
        p.Update(new LeaveTypeSpec(p.Key, p.Label, p.Colour, p.SortOrder, false, p.Pool, null, p.MaxDaysPerRequest, false, null, false,
            false, false, true, true), hasLeave: false, "test");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Approving the existing request must not be stranded by the switch.
        await Handler(db).Handle(Apply(staff.Id, "paternity", D(2026, 12, 5), D(2026, 12, 11), "approved", id, acting: approver.Id), CancellationToken.None);

        Assert.Equal("approved", (await db.LeaveRequests.AsNoTracking().SingleAsync(l => l.Id == id)).Status);
    }

    [Fact]
    public async Task Retyping_leave_into_a_deactivated_type_is_refused()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var approver = await AddStaff(db, "M1");
        await SaveCatalogue(db, s => s with { Active = false }, only: "compassionate");
        var leave = E.LeaveRequest.Create(staff.Id, "unpaid", D(2026, 11, 16), D(2026, 11, 16), null, null, 8m, null);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Apply(staff.Id, "compassionate", D(2026, 11, 16), D(2026, 11, 16), "pending", leave.Id, acting: approver.Id), CancellationToken.None));
    }

    [Fact]
    public async Task A_type_the_catalogue_does_not_have_is_invalid()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler(db).Handle(Apply(staff.Id, "sabbatical", D(2026, 11, 9), D(2026, 11, 9)), CancellationToken.None));
    }

    // ── privacy: a sensitive type is never named to other people or on a lock screen ──

    [Fact]
    public async Task A_sensitive_type_is_never_named_in_the_approvers_notification()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var approver = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);

        await Handler(db).Handle(Apply(staff.Id, "maternity", D(2026, 12, 14), D(2027, 3, 21)), CancellationToken.None);

        var note = Assert.Single(await db.Notifications.Where(n => n.EmployeeId == approver.Id).ToListAsync());
        Assert.DoesNotContain("maternity", note.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("applied for leave", note.Message);
    }

    [Fact]
    public async Task An_ordinary_type_is_still_named_as_before()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var approver = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m, sickHours: 14m));
        await db.SaveChangesAsync();

        await Handler(db).Handle(Apply(staff.Id, "annual", D(2026, 11, 9), D(2026, 11, 10)), CancellationToken.None);

        var note = Assert.Single(await db.Notifications.Where(n => n.EmployeeId == approver.Id).ToListAsync());
        Assert.Contains("annual leave", note.Message);
    }

    [Fact]
    public async Task The_decision_message_and_push_to_the_employee_do_not_name_a_sensitive_type()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var approver = await AddStaff(db, "M1");
        var push = new RecordingPush();
        var id = await Handler(db, push).Handle(Apply(staff.Id, "hospitalisation", D(2026, 11, 9), D(2026, 11, 10)), CancellationToken.None);

        await Handler(db, push).Handle(Apply(staff.Id, "hospitalisation", D(2026, 11, 9), D(2026, 11, 10), "approved", id, acting: approver.Id), CancellationToken.None);

        var note = await db.Notifications.SingleAsync(n => n.EmployeeId == staff.Id);
        Assert.DoesNotContain("hospitalisation", note.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(push.Bodies, b => Assert.DoesNotContain("hospitalisation", b, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(push.Bodies);
    }

    [Fact]
    public async Task Withdrawing_approved_sensitive_leave_does_not_name_the_type_to_managers()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        var approver = await AddStaff(db, "M1", "ROTA LEAD");
        await RoleSeed.GrantAsync(db, "ROTA LEAD", AppConstants.Permissions.LeaveApprove);
        var leave = E.LeaveRequest.Create(staff.Id, "paternity", D(2026, 12, 5), D(2026, 12, 11), null, null, 56m, null);
        leave.Update("paternity", D(2026, 12, 5), D(2026, 12, 11), null, null, 56m, null, "approved");
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();

        await new CancelLeaveCommandHandler(db, new Clock(), new NullHub()).Handle(new CancelLeaveCommand(leave.Id, staff.Id), CancellationToken.None);

        var note = Assert.Single(await db.Notifications.Where(n => n.EmployeeId == approver.Id).ToListAsync());
        Assert.DoesNotContain("paternity", note.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── one set of hours in every view ───────────────────────────────────────────

    [Fact]
    public async Task A_98_day_maternity_reads_the_same_hours_in_every_view_and_splits_by_year()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await Handler(db).Handle(Apply(staff.Id, "maternity", D(2026, 12, 14), D(2027, 3, 21)), CancellationToken.None);
        var clock = new Clock();

        foreach (var (year, expected) in new[] { (2026, 144m), (2027, 640m) })   // 18 days in 2026, 80 in 2027 = 98 days = 784h
        {
            var one = await new GetLeaveBalanceQueryHandler(db, clock).Handle(new GetLeaveBalanceQuery(staff.Id, year), CancellationToken.None);
            var all = (await new GetAllLeaveBalancesQueryHandler(db, clock).Handle(new GetAllLeaveBalancesQuery(year), CancellationToken.None)).Single(s => s.EmployeeId == staff.Id);
            var summary = (await new GetLeaveSummaryQueryHandler(db, clock).Handle(new GetLeaveSummaryQuery(year), CancellationToken.None)).Single(s => s.EmployeeId == staff.Id);

            Assert.Equal(expected, one.Types!.Single(t => t.Key == "maternity").Used);
            Assert.Equal(expected, all.Types!.Single(t => t.Key == "maternity").Used);
            Assert.Equal(expected, summary.Types!.Single(t => t.Key == "maternity").Used);
            Assert.Equal(expected, summary.TotalUsed);
            Assert.Null(one.Types!.Single(t => t.Key == "maternity").Remaining);   // no yearly balance
        }
    }

    [Fact]
    public async Task Maternity_is_not_counted_in_the_annual_or_sick_totals()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m, sickHours: 14m));
        await db.SaveChangesAsync();
        await Handler(db).Handle(Apply(staff.Id, "maternity", D(2026, 12, 14), D(2027, 3, 21)), CancellationToken.None);

        var b = await new GetLeaveBalanceQueryHandler(db, new Clock()).Handle(new GetLeaveBalanceQuery(staff.Id, 2026), CancellationToken.None);

        Assert.Equal(0m, b.AnnualUsed);
        Assert.Equal(80m, b.AnnualRemaining);
        Assert.Equal(0m, b.SickUsed);
    }

    [Fact]
    public async Task The_balance_lists_every_active_type_and_hides_a_deactivated_one_without_leave()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        await SaveCatalogue(db, s => s with { Active = false }, only: "compassionate");

        var b = await new GetLeaveBalanceQueryHandler(db, new Clock()).Handle(new GetLeaveBalanceQuery(staff.Id, 2026), CancellationToken.None);

        Assert.DoesNotContain(b.Types!, t => t.Key == "compassionate");
        Assert.Contains(b.Types!, t => t.Key == "maternity");
        Assert.Equal(8, b.Types!.Count);
    }

    // ── carry-over stays on the annual pool ──────────────────────────────────────

    [Fact]
    public async Task Carry_over_counts_every_type_charged_to_the_annual_pool_and_ignores_the_rest()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A1");
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 40m, sickHours: 14m));
        // 8h annual and 8h emergency are used (16h of 40h), plus a long maternity that must not count.
        foreach (var (type, day) in new[] { ("annual", D(2026, 3, 2)), ("emergency", D(2026, 3, 3)) })
        {
            var l = E.LeaveRequest.Create(staff.Id, type, day, day, null, null, 8m, null);
            l.Update(type, day, day, null, null, 8m, null, "approved");
            db.LeaveRequests.Add(l);
        }
        var m = E.LeaveRequest.Create(staff.Id, "maternity", D(2026, 5, 4), D(2026, 8, 9), null, null, 784m, null);
        m.Update("maternity", D(2026, 5, 4), D(2026, 8, 9), null, null, 784m, null, "approved");
        db.LeaveRequests.Add(m);
        await db.SaveChangesAsync();

        var carried = await new CarryOverLeaveEntitlementCommandHandler(db).Handle(new CarryOverLeaveEntitlementCommand(2026, 2027), CancellationToken.None);

        Assert.Equal(1, carried);
        var next = await db.LeaveEntitlements.SingleAsync(e => e.Year == 2027);
        Assert.Equal(14m, next.CarryOverHours);   // 40 - 16 = 24 left, capped at the policy's 14h
    }

    // ── no type name is compared outside the defaults ────────────────────────────

    [Fact]
    public void No_handler_compares_a_leave_types_name()
    {
        // The catalogue is the one place that knows what a type does. A name comparison anywhere else would
        // keep an old rule alive for the built-in names and silently skip a company's own types.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "backend", "src"))) root = root.Parent;
        var src = Path.Combine(root!.FullName, "backend", "src");

        var allowed = new[] { "LeaveTypeDefaults.cs", "LeaveTypeSpec.cs", "AppConstants.cs", "Migrations", "SeedData.cs" };   // LeaveTypeSpec names the pools (annual, sick), which are not types
        var pattern = new System.Text.RegularExpressions.Regex(
            "\"(annual|sick|emergency|unpaid|no-show|maternity|paternity|hospitalisation|compassionate)\"|AppConstants\\.Leave\\.(Annual|Sick|Emergency|Unpaid|NoShow)\\b");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !allowed.Any(a => f.Contains(a)))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, n: i + 1))
                .Where(x => pattern.IsMatch(x.line) && !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("///")))
            .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.n}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Leave-type names compared outside LeaveTypeDefaults:\n" + string.Join("\n", offenders));
    }
}
