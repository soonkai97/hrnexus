using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// The rejecting path of the daily auto-reject job. It flips status with ExecuteUpdateAsync, which
/// EF InMemory cannot run, so it is proven here against real PostgreSQL. The job's clock is set to
/// January 2020 so it can only reject leave this test created (other tests use 2026 dates).
/// </summary>
[Collection("postgres")]
public class LeaveJobsTests(PostgresFixture fx)
{
    private sealed class FixedClock(DateTime localNow) : IDateTimeProvider
    {
        public DateTime UtcNow => localNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(localNow);
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

    private async Task<Guid> SeedStaffAsync(string staffId)
    {
        await using var db = fx.NewDb();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "BARTENDER");
        if (role is null)
        {
            role = E.Role.Create("bartender");
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        var staff = E.Staff.Create(staffId, "Job", "Tester", $"{staffId}@t.com", "hash", new List<Guid> { role.Id });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
    }

    private static DateTime Day(int month, int day) => new(2020, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AutoReject_RejectsOnlyPendingLeaveThatEnded_StampsTheNote_AndNotifiesOnce()
    {
        var empId = await SeedStaffAsync("LJOB01");

        var overdue = E.LeaveRequest.Create(empId, AppConstants.Leave.Unpaid, Day(1, 6), Day(1, 8), null, null, 8m, "please");
        var endsToday = E.LeaveRequest.Create(empId, AppConstants.Leave.Unpaid, Day(1, 9), Day(1, 10), null, null, 8m, null);
        var approved = E.LeaveRequest.Create(empId, AppConstants.Leave.Unpaid, Day(1, 2), Day(1, 3), null, null, 8m, null);
        approved.Update(AppConstants.Leave.Unpaid, Day(1, 2), Day(1, 3), null, null, 8m, null, AppConstants.Leave.Approved);

        await using (var seed = fx.NewDb())
        {
            seed.LeaveRequests.AddRange(overdue, endsToday, approved);
            await seed.SaveChangesAsync();
        }

        var push = new RecordingPush();
        await using (var db = fx.NewDb())
        {
            var jobs = new LeaveJobs(db, new FixedClock(new DateTime(2020, 1, 10, 2, 0, 0, DateTimeKind.Utc)), push,
                NullLogger<LeaveJobs>.Instance);
            await jobs.AutoRejectOverdueLeaveAsync();
        }

        await using var check = fx.NewDb();
        var rows = await check.LeaveRequests.Where(l => l.EmployeeId == empId).ToDictionaryAsync(l => l.Id);

        Assert.Equal(AppConstants.Leave.Rejected, rows[overdue.Id].Status);
        Assert.Contains("please", rows[overdue.Id].Notes);
        Assert.Contains("Auto-rejected 2020-01-10", rows[overdue.Id].Notes);
        Assert.Equal(AppConstants.Leave.Pending, rows[endsToday.Id].Status);
        Assert.Equal(AppConstants.Leave.Approved, rows[approved.Id].Status);
        Assert.Equal(new[] { empId }, push.Sent);
    }

    [Fact]
    public async Task AutoReject_IsIdempotent_ASecondRunRejectsAndNotifiesNothingMore()
    {
        var empId = await SeedStaffAsync("LJOB02");
        var overdue = E.LeaveRequest.Create(empId, AppConstants.Leave.Unpaid, Day(2, 3), Day(2, 4), null, null, 8m, null);
        await using (var seed = fx.NewDb())
        {
            seed.LeaveRequests.Add(overdue);
            await seed.SaveChangesAsync();
        }

        var push = new RecordingPush();
        for (var run = 0; run < 2; run++)
        {
            await using var db = fx.NewDb();
            await new LeaveJobs(db, new FixedClock(new DateTime(2020, 2, 10, 2, 0, 0, DateTimeKind.Utc)), push,
                NullLogger<LeaveJobs>.Instance).AutoRejectOverdueLeaveAsync();
        }

        await using var check = fx.NewDb();
        Assert.Equal(AppConstants.Leave.Rejected, (await check.LeaveRequests.SingleAsync(l => l.Id == overdue.Id)).Status);
        Assert.Equal(1, push.Sent.Count(id => id == empId));
    }
}
