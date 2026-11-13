using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Infrastructure.Jobs;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// LeaveJobs: the Jan-1 entitlement reset and the daily auto-reject of overdue pending leave.
/// Why these matter: the reset is the only thing that gives staff a balance row each year, and the
/// auto-reject is the only thing that stops forgotten requests from sitting pending forever.
/// The rejecting path flips status with ExecuteUpdateAsync, which the EF InMemory provider cannot
/// run, so this file covers its "nothing to reject" branches; the rejecting path runs against real
/// PostgreSQL in Integration.Tests/LeaveJobsTests.
/// </summary>
public class LeaveJobsTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class FixedClock(DateTime localNow, DateTime? utcNow = null) : IDateTimeProvider
    {
        public DateTime UtcNow => utcNow ?? localNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => localNow;
    }

    private sealed class RecordingPush : IPushNotificationService
    {
        public List<Guid> Sent { get; } = new();
        public Task SendAsync(Guid id, string t, string b, string? u = null, CancellationToken ct = default)
        {
            Sent.Add(id);
            return Task.CompletedTask;
        }
        public Task SendToManyAsync(IEnumerable<Guid> ids, string t, string b, string? u = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static LeaveJobs MakeJobs(TestDbContext db, DateTime localNow, RecordingPush? push = null, DateTime? utcNow = null)
        => new(db, new FixedClock(localNow, utcNow), push ?? new RecordingPush(), NullLogger<LeaveJobs>.Instance);

    private static async Task<E.Staff> AddStaff(TestDbContext db, string id, bool active = true)
    {
        var role = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        var staff = E.Staff.Create(id, "Test", id, $"{id.ToLower()}@test.com", "hash", new List<Guid> { role });
        if (!active) staff.Deactivate();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    private static E.LeaveRequest MakeLeave(Guid empId, string status, DateTime start, DateTime end, string? notes = null)
    {
        var leave = E.LeaveRequest.Create(empId, "annual", start, end, null, null, null, notes);
        if (status != "pending")
            leave.Update("annual", start, end, null, null, null, notes, status);
        return leave;
    }

    // ── ResetLeaveEntitlementsAsync ───────────────────────────────────────────

    [Fact]
    public async Task ResetEntitlements_CreatesOneRowPerActiveStaff_ForTheCurrentYear()
    {
        await using var db = CreateDb();
        var a = await AddStaff(db, "A001");
        var b = await AddStaff(db, "A002");

        await MakeJobs(db, new DateTime(2027, 1, 1, 0, 5, 0)).ResetLeaveEntitlementsAsync();

        var rows = await db.LeaveEntitlements.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(2027, r.Year));
        Assert.Contains(rows, r => r.EmployeeId == a.Id);
        Assert.Contains(rows, r => r.EmployeeId == b.Id);
    }

    [Fact]
    public async Task ResetEntitlements_SkipsDeactivatedStaff()
    {
        await using var db = CreateDb();
        await AddStaff(db, "A001");
        var left = await AddStaff(db, "A002", active: false);

        await MakeJobs(db, new DateTime(2027, 1, 1)).ResetLeaveEntitlementsAsync();

        Assert.DoesNotContain(await db.LeaveEntitlements.ToListAsync(), r => r.EmployeeId == left.Id);
        Assert.Single(await db.LeaveEntitlements.ToListAsync());
    }

    [Fact]
    public async Task ResetEntitlements_IsIdempotent_ASecondRunCreatesNothing()
    {
        await using var db = CreateDb();
        await AddStaff(db, "A001");
        var jobs = MakeJobs(db, new DateTime(2027, 1, 1));

        await jobs.ResetLeaveEntitlementsAsync();
        await jobs.ResetLeaveEntitlementsAsync();

        Assert.Single(await db.LeaveEntitlements.ToListAsync());
    }

    [Fact]
    public async Task ResetEntitlements_KeepsAnAdminSetRowForThatYearUntouched()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A001");
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2027, annualHours: 112m, sickHours: 112m));
        await db.SaveChangesAsync();

        await MakeJobs(db, new DateTime(2027, 1, 1)).ResetLeaveEntitlementsAsync();

        var row = Assert.Single(await db.LeaveEntitlements.ToListAsync());
        Assert.Equal(112m, row.AnnualHours);
        Assert.Equal(112m, row.SickHours);
    }

    [Fact]
    public async Task ResetEntitlements_OnlyAddsTheNewYear_PreviousYearRowIsUntouched()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A001");
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 96m, sickHours: 96m));
        await db.SaveChangesAsync();

        await MakeJobs(db, new DateTime(2027, 1, 1)).ResetLeaveEntitlementsAsync();

        var rows = await db.LeaveEntitlements.OrderBy(r => r.Year).ToListAsync();
        Assert.Equal(new[] { 2026, 2027 }, rows.Select(r => r.Year));
        Assert.Equal(96m, rows[0].AnnualHours);
    }

    [Fact]
    public async Task ResetEntitlements_NewRowsStartAtZeroHours()
    {
        // Characterises current behaviour: the job creates EMPTY rows (no policy entitlement, no
        // carry-over); an admin must still set the hours. The leave policy phase replaces this.
        await using var db = CreateDb();
        await AddStaff(db, "A001");

        await MakeJobs(db, new DateTime(2027, 1, 1)).ResetLeaveEntitlementsAsync();

        var row = Assert.Single(await db.LeaveEntitlements.ToListAsync());
        Assert.Equal(0m, row.AnnualHours);
        Assert.Equal(0m, row.SickHours);
    }

    [Fact]
    public async Task ResetEntitlements_UsesTheVenueLocalYear_NotTheUtcYear()
    {
        // 00:30 on 1 Jan at the venue is still 31 Dec in UTC; the new year must be the venue's.
        await using var db = CreateDb();
        await AddStaff(db, "A001");

        await MakeJobs(db,
            localNow: new DateTime(2027, 1, 1, 0, 30, 0),
            utcNow: new DateTime(2026, 12, 31, 16, 30, 0)).ResetLeaveEntitlementsAsync();

        Assert.Equal(2027, Assert.Single(await db.LeaveEntitlements.ToListAsync()).Year);
    }

    // ── AutoRejectOverdueLeaveAsync (branches that reject nothing) ────────────

    [Fact]
    public async Task AutoReject_LeavesPendingLeaveThatHasNotEnded()
    {
        // The cut-off is the END date: a leave ending today or later is still live.
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A001");
        var today = new DateTime(2027, 3, 10);
        var endsToday = MakeLeave(staff.Id, "pending", today.AddDays(-2), today);
        var future = MakeLeave(staff.Id, "pending", today.AddDays(5), today.AddDays(6));
        db.LeaveRequests.AddRange(endsToday, future);
        await db.SaveChangesAsync();
        var push = new RecordingPush();

        await MakeJobs(db, today.AddHours(2), push).AutoRejectOverdueLeaveAsync();

        Assert.All(await db.LeaveRequests.ToListAsync(), l => Assert.Equal("pending", l.Status));
        Assert.Empty(push.Sent);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    public async Task AutoReject_NeverTouchesLeaveThatIsNotPending(string status)
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db, "A001");
        var today = new DateTime(2027, 3, 10);
        var old = MakeLeave(staff.Id, status, today.AddDays(-10), today.AddDays(-8), notes: "original note");
        db.LeaveRequests.Add(old);
        await db.SaveChangesAsync();
        var push = new RecordingPush();

        await MakeJobs(db, today.AddHours(2), push).AutoRejectOverdueLeaveAsync();

        var row = await db.LeaveRequests.SingleAsync();
        Assert.Equal(status, row.Status);
        Assert.Equal("original note", row.Notes);
        Assert.Empty(push.Sent);
    }
}
