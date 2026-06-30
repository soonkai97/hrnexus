using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Queries;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// GetStaffPayslipQuery — the per-staff, per-shift pay breakdown (drill-down behind the Payroll
/// summary). WHY these tests matter:
///  - The owner reads this to see exactly which shifts make up a person's pay; a wrong row or a
///    leaked open/out-of-range shift misstates what's owed.
///  - The period total must equal the sum of the listed shifts (and a fixed shift must price flat,
///    not by hours) — the figure has to reconcile with the summary's Gross Pay.
/// </summary>
public class PayslipQueryTests
{
    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static E.Staff MakeStaff(string staffId = "S001", string first = "Alice") =>
        E.Staff.Create(staffId, first, "Wong", $"{staffId}@test.com", "hash", new List<string> { "BARTENDER" });

    private static E.WorkLog CompletedShift(Guid empId, DateOnly date, string start, string end, decimal rate,
        string payType = AppConstants.PayType.Hourly, string role = "BARTENDER")
    {
        var log = E.WorkLog.StartLive(empId, role, "loc1", "Main", date, start, rate, payType: payType);
        log.End(end);
        return log;
    }

    [Fact]
    public async Task ReturnsStaffIdentity_AndEmptyShifts_WhenNoCompletedShiftsInRange()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);
        var result  = await handler.Handle(
            new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
            CancellationToken.None);

        Assert.Equal("S001", result.StaffId);
        Assert.Equal("Alice Wong", result.StaffName);
        Assert.Empty(result.Shifts);
        Assert.Equal(0m, result.TotalHours);
        Assert.Equal(0m, result.TotalPay);
    }

    [Fact]
    public async Task ListsEachShift_DayOrdered_WithHoursPay_AndTotalsThatReconcile()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);

        // Added out of order; expect output ordered by date then start time.
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 10), "09:00", "13:00", 20m)); // 4h × 20 = 80
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 5),  "09:00", "17:00", 20m)); // 8h × 20 = 160
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);
        var result  = await handler.Handle(
            new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
            CancellationToken.None);

        Assert.Equal(2, result.Shifts.Count);
        Assert.Equal("2025-06-05", result.Shifts[0].WorkDate);   // earlier date first
        Assert.Equal(8m,   result.Shifts[0].Hours);
        Assert.Equal(160m, result.Shifts[0].Pay);
        Assert.Equal("2025-06-10", result.Shifts[1].WorkDate);
        Assert.Equal(80m,  result.Shifts[1].Pay);
        // Totals reconcile with the listed rows
        Assert.Equal(12m,  result.TotalHours);
        Assert.Equal(240m, result.TotalPay);
    }

    [Fact]
    public async Task FixedShift_PricesFlat_NotHoursTimesRate()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);
        // 12-hour overnight package at RM500 flat (hours-based would be 6000).
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 7), "20:00", "08:00", 500m,
            payType: AppConstants.PayType.Fixed, role: "PACKAGE"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);
        var result  = await handler.Handle(
            new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
            CancellationToken.None);

        var row = Assert.Single(result.Shifts);
        Assert.Equal("fixed", row.PayType);
        Assert.Equal(12m, row.Hours);     // hours are still reported for the record
        Assert.Equal(500m, row.Pay);      // but pay is the flat fee
        Assert.Equal(500m, result.TotalPay);
    }

    [Fact]
    public async Task ExcludesOpenShifts_AndShiftsOutsideTheRange()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);

        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 15), "09:00", "17:00", 20m)); // in range
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 5, 31), "09:00", "17:00", 20m)); // before range
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 7, 1),  "09:00", "17:00", 20m)); // after range
        // Open shift in range (not ended) — must not appear
        db.WorkLogs.Add(E.WorkLog.StartLive(alice.Id, "BARTENDER", "loc1", "Main", new DateOnly(2025, 6, 20), "20:00", 20m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);
        var result  = await handler.Handle(
            new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
            CancellationToken.None);

        var row = Assert.Single(result.Shifts);
        Assert.Equal("2025-06-15", row.WorkDate);
    }

    [Fact]
    public async Task NetPayable_DeductsAllUnsettledAdvances_ExcludesSettled()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 5),  "09:00", "17:00", 20m)); // 160
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 10), "09:00", "13:00", 20m)); // 80 → gross 240
        db.CashAdvances.Add(E.CashAdvance.Create(alice.Id, 100m, new DateOnly(2025, 6, 1), "advance", "admin"));
        var settled = E.CashAdvance.Create(alice.Id, 50m, new DateOnly(2025, 5, 1), "old", "admin");
        settled.MarkSettled();   // already deducted → must NOT reduce net
        db.CashAdvances.Add(settled);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);
        var result  = await handler.Handle(
            new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
            CancellationToken.None);

        Assert.Equal(240m, result.TotalPay);
        Assert.Equal(100m, result.OutstandingAdvance);   // settled 50 excluded
        Assert.Equal(140m, result.NetPayable);           // 240 − 100
    }

    [Fact]
    public async Task NetPayable_FlooredAtZero_WhenAdvanceExceedsGross()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 5), "09:00", "17:00", 20m)); // 160 gross
        db.CashAdvances.Add(E.CashAdvance.Create(alice.Id, 500m, new DateOnly(2025, 6, 1), "big advance", "admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);
        var result  = await handler.Handle(
            new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
            CancellationToken.None);

        Assert.Equal(160m, result.TotalPay);
        Assert.Equal(500m, result.OutstandingAdvance);
        Assert.Equal(0m,   result.NetPayable);   // never negative; remainder carries as still-outstanding
    }

    [Fact]
    public async Task JobBreakdown_GroupsByJobAndPayType_OrderedByPayDesc()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);
        // Two hourly BARTENDER shifts (160 + 80 = 240) and one fixed PACKAGE shift (flat 500).
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 5),  "09:00", "17:00", 20m));
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 6),  "09:00", "13:00", 20m));
        db.WorkLogs.Add(CompletedShift(alice.Id, new DateOnly(2025, 6, 7),  "20:00", "08:00", 500m,
            payType: AppConstants.PayType.Fixed, role: "PACKAGE"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);
        var result  = await handler.Handle(
            new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
            CancellationToken.None);

        Assert.Equal(2, result.JobBreakdown.Count);
        // PACKAGE (500) sorts above BARTENDER (240).
        Assert.Equal("PACKAGE", result.JobBreakdown[0].Role);
        Assert.Equal("fixed",   result.JobBreakdown[0].PayType);
        Assert.Equal(1,    result.JobBreakdown[0].ShiftCount);
        Assert.Equal(500m, result.JobBreakdown[0].Pay);
        Assert.Equal("BARTENDER", result.JobBreakdown[1].Role);
        Assert.Equal(2,    result.JobBreakdown[1].ShiftCount);
        Assert.Equal(12m,  result.JobBreakdown[1].Hours);
        Assert.Equal(240m, result.JobBreakdown[1].Pay);
    }

    [Fact]
    public async Task Throws_WhenStaffNotFound()
    {
        await using var db = CreateDb();
        var handler = new GetStaffPayslipQueryHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetStaffPayslipQuery("NOPE", new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Throws_WhenEndDateBeforeStartDate()
    {
        await using var db = CreateDb();
        var alice = MakeStaff();
        db.Staff.Add(alice);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetStaffPayslipQueryHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new GetStaffPayslipQuery(alice.StaffId, new DateOnly(2025, 6, 30), new DateOnly(2025, 6, 1)),
                CancellationToken.None));
    }
}
