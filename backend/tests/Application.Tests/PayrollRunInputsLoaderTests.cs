using HRNexus.Application.Payroll;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Who is in a month's payroll run, and what the database says about each of them.
///
/// WHY these tests matter: a run that silently skips someone, includes someone who left last year, or mixes one
/// person's leave or advance into another's pay is the worst kind of payroll error: nothing crashes and every
/// payslip looks right. People who cannot be paid yet (no salary) are listed, never skipped.
/// </summary>
public class PayrollRunInputsLoaderTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<E.Staff> AddStaffAsync(TestDbContext db, string staffId, string? klass = "citizen",
        DateOnly? start = null, DateOnly? end = null, bool active = true, DateTime? dob = null, bool salary = true,
        DateOnly? salaryFrom = null, decimal basic = 3000m)
    {
        var s = E.Staff.Create(staffId, "First", staffId, $"{staffId.ToLower()}@t.com", "hash", [], dateOfBirth: dob ?? new DateTime(1990, 3, 4));
        if (klass is not null && start is not null) s.SetEmployment(klass, start.Value, end);
        if (!active) s.Deactivate();
        db.Staff.Add(s);
        if (salary) db.SalaryProfiles.Add(E.SalaryProfile.Create(s.Id, salaryFrom ?? new DateOnly(2020, 1, 1), basic, "monthly", null, null, null));
        await db.SaveChangesAsync();
        return s;
    }

    private static E.LeaveRequest LeaveOf(Guid employee, string type, string from, string? to = null, string status = "approved", decimal? hours = 8m)
    {
        var l = E.LeaveRequest.Create(employee, type, DateTime.Parse(from), DateTime.Parse(to ?? from), null, null, hours, null);
        l.Update(type, DateTime.Parse(from), DateTime.Parse(to ?? from), null, null, hours, null, status);
        return l;
    }

    // ── who is in the run ────────────────────────────────────────────────────────

    [Fact]
    public async Task Someone_employed_all_month_with_a_salary_is_in_the_run_with_their_details()
    {
        await using var db = CreateDb();
        var eve = await AddStaffAsync(db, "EVE1", start: new DateOnly(2024, 1, 1));

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        var p = Assert.Single(loaded.People);
        Assert.Equal((eve.Id, "EVE1", "citizen", new DateOnly(1990, 3, 4), new DateOnly(2024, 1, 1)),
            (p.EmployeeId, p.StaffId, p.CitizenshipStatus, p.DateOfBirth, p.EmploymentStart));
        Assert.Equal("First EVE1", p.Name);
        Assert.Empty(loaded.NotInRun);
    }

    [Fact]
    public async Task People_who_left_in_the_month_or_are_now_inactive_are_still_paid_for_it()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "LEFT1", start: new DateOnly(2024, 1, 1), end: new DateOnly(2026, 6, 10), active: false);

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        Assert.Single(loaded.People);
    }

    [Fact]
    public async Task Someone_whose_employment_ended_before_the_month_or_starts_after_it_is_not_in_it_and_not_reported()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "OLD1", start: new DateOnly(2020, 1, 1), end: new DateOnly(2026, 5, 31));
        await AddStaffAsync(db, "NEW1", start: new DateOnly(2026, 7, 1));

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        Assert.Empty(loaded.People);
        Assert.Empty(loaded.NotInRun);
    }

    /// <summary>WHY: nobody is silently skipped. Someone who works this month but has no salary is listed so the preparer can fix it.</summary>
    [Fact]
    public async Task Someone_employed_in_the_month_with_no_salary_is_listed_as_not_in_the_run()
    {
        await using var db = CreateDb();
        var nosal = await AddStaffAsync(db, "NOSAL1", start: new DateOnly(2024, 1, 1), salary: false);

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        Assert.Empty(loaded.People);
        var n = Assert.Single(loaded.NotInRun);
        Assert.Equal((nosal.Id, "NOSAL1"), (n.EmployeeId, n.StaffId));
        Assert.Contains("salary", n.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_salary_that_only_starts_after_the_person_stopped_working_does_not_count_as_a_salary_for_the_month()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "LATE1", start: new DateOnly(2024, 1, 1), end: new DateOnly(2026, 6, 10), salaryFrom: new DateOnly(2026, 6, 20));

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        Assert.Empty(loaded.People);
        Assert.Single(loaded.NotInRun);
    }

    [Fact]
    public async Task A_salary_that_starts_part_way_through_the_month_puts_the_person_in_the_run()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "MID1", start: new DateOnly(2024, 1, 1), salaryFrom: new DateOnly(2026, 6, 20));

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        Assert.Single(loaded.People);
    }

    [Fact]
    public async Task Someone_with_a_salary_but_no_employment_dates_is_in_the_run_and_flagged_later_by_the_calculator()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "NODATES1", klass: null);

        var p = Assert.Single((await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None)).People);

        Assert.Null(p.EmploymentStart);
        Assert.Null(p.CitizenshipStatus);
    }

    [Fact]
    public async Task People_are_listed_in_staff_id_order()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "B2", start: new DateOnly(2024, 1, 1));
        await AddStaffAsync(db, "A1", start: new DateOnly(2024, 1, 1));

        var ids = (await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None)).People.Select(p => p.StaffId);

        Assert.Equal(["A1", "B2"], ids);
    }

    // ── what is loaded for each person ───────────────────────────────────────────

    [Fact]
    public async Task Leave_is_loaded_per_person_and_only_when_it_could_matter_for_the_month()
    {
        await using var db = CreateDb();
        var a = await AddStaffAsync(db, "A1", start: new DateOnly(2024, 1, 1));
        var b = await AddStaffAsync(db, "B1", start: new DateOnly(2024, 1, 1));
        db.LeaveRequests.AddRange(
            LeaveOf(a.Id, "unpaid", "2026-06-10"),
            LeaveOf(a.Id, "unpaid", "2026-06-11", status: "pending"),
            LeaveOf(a.Id, "unpaid", "2026-06-12", status: "rejected"),
            LeaveOf(a.Id, "unpaid", "2026-06-15", status: "cancelled"),
            LeaveOf(a.Id, "unpaid", "2026-05-04"),                       // last month
            LeaveOf(a.Id, "unpaid", "2026-07-06"),                       // next month
            LeaveOf(a.Id, "unpaid", "2026-05-28", "2026-06-02"),         // runs into the month
            LeaveOf(b.Id, "unpaid", "2026-06-17"));
        await db.SaveChangesAsync();

        var people = (await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None)).People.ToDictionary(p => p.StaffId);

        Assert.Equal(["2026-05-28", "2026-06-10", "2026-06-11"], people["A1"].Leave.Select(l => l.Start.ToString("yyyy-MM-dd")).OrderBy(x => x));
        Assert.Equal(1, people["A1"].Leave.Count(l => l.Status == "pending"));
        Assert.Equal(new DateOnly(2026, 6, 17), Assert.Single(people["B1"].Leave).Start);
    }

    [Fact]
    public async Task A_leave_row_keeps_its_id_type_hours_and_dates()
    {
        await using var db = CreateDb();
        var a = await AddStaffAsync(db, "A1", start: new DateOnly(2024, 1, 1));
        var leave = LeaveOf(a.Id, "Unpaid", "2026-06-10", hours: 4m);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();

        var row = Assert.Single(Assert.Single((await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None)).People).Leave);

        Assert.Equal((leave.Id, "unpaid", new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10), (decimal?)4m), (row.Id, row.Type, row.Start, row.End, row.Hours));
    }

    [Fact]
    public async Task Only_pay_items_in_force_in_the_month_are_loaded()
    {
        await using var db = CreateDb();
        var a = await AddStaffAsync(db, "A1", start: new DateOnly(2024, 1, 1));
        var inForce = E.PayItem.Create(a.Id, E.PayItemKind.Allowance, "Transport", 100m, true, true, 0, new DateOnly(2026, 1, 1), null, "admin");
        var ended = E.PayItem.Create(a.Id, E.PayItemKind.Allowance, "Old", 100m, true, true, 0, new DateOnly(2025, 1, 1), new DateOnly(2026, 5, 31), "admin");
        var future = E.PayItem.Create(a.Id, E.PayItemKind.Allowance, "Soon", 100m, true, true, 0, new DateOnly(2026, 7, 1), null, "admin");
        db.PayItems.AddRange(inForce, ended, future);
        await db.SaveChangesAsync();

        var items = Assert.Single((await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None)).People).PayItems;

        Assert.Equal(inForce.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task Only_outstanding_advances_are_loaded_with_what_has_already_been_repaid()
    {
        await using var db = CreateDb();
        var a = await AddStaffAsync(db, "A1", start: new DateOnly(2024, 1, 1));
        var open = E.CashAdvance.Create(a.Id, 600m, new DateOnly(2026, 4, 2), "Rent", "admin");
        open.SetRepaymentPerMonth(150m);
        var settled = E.CashAdvance.Create(a.Id, 100m, new DateOnly(2026, 1, 2), null, "admin");
        settled.MarkSettled();
        db.CashAdvances.AddRange(open, settled);
        var run = E.PayrollRun.Start(2026, 4, Guid.NewGuid(), new E.PayrollRunBasis("1", 26m, 8m, 0.5m, 0), DateTime.UtcNow);
        db.PayrollRuns.Add(run);
        db.CashAdvanceRepayments.AddRange(
            E.CashAdvanceRepayment.Create(open.Id, run.Id, 150m, DateTime.UtcNow),
            E.CashAdvanceRepayment.Create(open.Id, Guid.NewGuid(), 100.50m, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var adv = Assert.Single(Assert.Single((await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None)).People).Advances);

        Assert.Equal((open.Id, 600m, 150m, 250.50m), (adv.Id, adv.Amount, adv.RepaymentPerMonth, adv.Repaid));
    }

    [Fact]
    public async Task Salary_rows_are_loaded_oldest_first()
    {
        await using var db = CreateDb();
        var a = await AddStaffAsync(db, "A1", start: new DateOnly(2024, 1, 1), salaryFrom: new DateOnly(2025, 1, 1), basic: 2500m);
        db.SalaryProfiles.Add(E.SalaryProfile.Create(a.Id, new DateOnly(2024, 1, 1), 2000m, "monthly", null, null, null));
        await db.SaveChangesAsync();

        var rows = Assert.Single((await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None)).People).Salaries;

        Assert.Equal([2000m, 2500m], rows.Select(r => r.Basic));
    }

    // ── the month's settings ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_month_takes_its_divisor_working_week_and_catalogue_from_the_company_and_the_rates_from_the_rate_table()
    {
        await using var db = CreateDb();
        db.StatutoryRateTables.Add(E.StatutoryRateTable.CreatePercentage(HRNexus.Domain.Payroll.StatutoryScheme.Epf, "citizen",
            new DateOnly(2025, 1, 1), 0.11m, 0.13m, HRNexus.Domain.Payroll.RoundingRule.NearestCent, "test"));
        await db.SaveChangesAsync();

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        Assert.Equal((2026, 6, 26m, 8m, 0.5m), (loaded.Month.Year, loaded.Month.Month, loaded.Month.Divisor, loaded.Month.Company.WorkdayHours, loaded.Month.DeductionCapRate));
        Assert.NotNull(loaded.Month.Catalog.Find("unpaid"));
        Assert.Single(loaded.Month.Rates);
        Assert.Equal(0, loaded.PolicyVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task A_month_that_does_not_exist_is_refused(int month)
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<ArgumentException>(() => PayrollRunInputs.LoadAsync(db, 2026, month, CancellationToken.None));
    }

    [Fact]
    public void The_deduction_cap_is_fifty_percent_of_gross()
    {
        Assert.Equal(0.5m, HRNexus.Domain.Constants.AppConstants.Payroll.DeductionCapRate);
    }
}
