using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7l (D2): the yearly entitlement (AnnualHours) and the hours carried over from last year
/// (CarryOverHours) are stored separately and the usable annual pool is their sum. Editing the
/// entitlement can therefore never delete carried-over hours.
/// </summary>
public class LeaveEntitlementTests
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
        private static readonly DateTime Now = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(Now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => Now;
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<E.Staff> AddStaff(TestDbContext db)
    {
        var roleId = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        var staff = E.Staff.Create("C1", "Carry", "Over", "c1@t.com", "hash", new List<Guid> { roleId });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    // ── Domain ────────────────────────────────────────────────────────────────

    [Fact]
    public void AddCarryOver_RaisesCarryOverHours_WithoutTouchingTheBaseEntitlement()
    {
        var ent = E.LeaveEntitlement.Create(Guid.NewGuid(), 2026, annualHours: 80m);

        ent.AddCarryOver(8m);

        Assert.Equal(80m, ent.AnnualHours);
        Assert.Equal(8m, ent.CarryOverHours);
        Assert.Equal(88m, ent.TotalAnnualHours);
    }

    [Fact]
    public void Update_ChangesTheBase_AndKeepsCarryOverInTheUsablePool()
    {
        var ent = E.LeaveEntitlement.Create(Guid.NewGuid(), 2026, annualHours: 80m);
        ent.AddCarryOver(8m);

        ent.Update(annualHours: 100m, sickHours: 14m);

        Assert.Equal(8m, ent.CarryOverHours);
        Assert.Equal(108m, ent.TotalAnnualHours);
    }

    // ── Admin edit after a carry-over ─────────────────────────────────────────

    [Fact]
    public async Task EditingTheEntitlementAfterCarryOver_DoesNotDeleteTheCarryOver()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var ent = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m);
        ent.AddCarryOver(8m);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new UpsertLeaveEntitlementCommandHandler(db)
            .Handle(new UpsertLeaveEntitlementCommand(staff.Id, 2026, 100m, 14m), CancellationToken.None);

        db.ChangeTracker.Clear();
        var saved = await db.LeaveEntitlements.FirstAsync(e => e.EmployeeId == staff.Id);
        Assert.Equal(100m, saved.AnnualHours);
        Assert.Equal(8m, saved.CarryOverHours);
        Assert.Equal(108m, saved.TotalAnnualHours);
    }

    // ── Readers use the pool ──────────────────────────────────────────────────

    [Fact]
    public async Task Balance_ReportsTheTotalPool_AndRemainingIncludesCarryOver()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var ent = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m);
        ent.AddCarryOver(8m);
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var balance = await new GetLeaveBalanceQueryHandler(db, new Clock())
            .Handle(new GetLeaveBalanceQuery(staff.Id, 2026), CancellationToken.None);

        Assert.Equal(88m, balance.AnnualEntitlement);
        Assert.Equal(8m, balance.CarryOverHours);
        Assert.Equal(88m, balance.AnnualRemaining);
    }

    [Fact]
    public async Task Apply_CanUseCarriedOverHours_ButNotMoreThanThePool()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var ent = E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 8m);
        ent.AddCarryOver(8m);   // pool = 16h
        db.LeaveEntitlements.Add(ent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var handler = new UpsertLeaveCommandHandler(db, new NullHub(), new NullPush(), new NullAudit(), new Clock());

        // Mon-Tue 16h fits only because of the carry-over.
        var id = await handler.Handle(new UpsertLeaveCommand(null, staff.Id, "annual", D(2026, 6, 1), D(2026, 6, 2),
            null, null, null, null, "pending"), CancellationToken.None);
        Assert.NotEqual(Guid.Empty, id);

        // One more day would take the person to 24h of a 16h pool.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpsertLeaveCommand(null, staff.Id, "annual", D(2026, 6, 3), D(2026, 6, 3),
                null, null, null, null, "pending"), CancellationToken.None));
    }

    [Fact]
    public async Task CarryOver_ComesFromTheWholePoolOfTheClosingYear_AndLeavesTheNewBaseAlone()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var from = E.LeaveEntitlement.Create(staff.Id, 2025, annualHours: 40m);
        from.AddCarryOver(10m);   // closing-year pool = 50h, nothing used
        db.LeaveEntitlements.Add(from);
        db.LeaveEntitlements.Add(E.LeaveEntitlement.Create(staff.Id, 2026, annualHours: 80m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new CarryOverLeaveEntitlementCommandHandler(db)
            .Handle(new CarryOverLeaveEntitlementCommand(2025, 2026, 14m), CancellationToken.None);

        db.ChangeTracker.Clear();
        var to = await db.LeaveEntitlements.FirstAsync(e => e.Year == 2026);
        Assert.Equal(80m, to.AnnualHours);       // the base is never inflated
        Assert.Equal(14m, to.CarryOverHours);    // 50h left, capped at 14
    }
}
