using HRNexus.Application.Payroll;
using HRNexus.Domain.Entities;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// The payroll run's input loader and calculator against a real PostgreSQL, where salary, date of birth and
/// repayment amounts are ciphertext and dates have a time zone.
///
/// WHY these tests matter: the loader filters by date in SQL and sums encrypted amounts in memory. InMemory cannot
/// prove either: only the real provider shows that the date comparisons translate, that a leave that ends on the
/// last instant of the month is found, and that the decrypted figures feed the calculator exactly.
/// </summary>
[Collection("postgres")]
public class PayrollRunInputsPostgresTests(PostgresFixture fx)
{
    private async Task<AppDbContext> FreshAsync()
    {
        var db = fx.NewDbOn($"inputs_{Guid.NewGuid():N}");
        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task A_whole_month_loads_and_calculates_from_encrypted_columns_exactly()
    {
        await using var db = await FreshAsync();
        var eve = Staff.Create("EVE1", "Eve", "Tan", null, "hash", [], dateOfBirth: new DateTime(1990, 3, 4));
        eve.SetEmployment("citizen", new DateOnly(2024, 1, 1), null);
        db.Staff.Add(eve);
        db.SalaryProfiles.Add(SalaryProfile.Create(eve.Id, new DateOnly(2024, 1, 1), 2600m, "monthly", null, null, null));
        db.SalaryProfiles.Add(SalaryProfile.Create(eve.Id, new DateOnly(2026, 6, 16), 5200m, "monthly", null, null, null));
        var leave = LeaveRequest.Create(eve.Id, "unpaid", new DateTime(2026, 6, 10), new DateTime(2026, 6, 10), null, null, 4m, null);
        leave.Update("unpaid", new DateTime(2026, 6, 10), new DateTime(2026, 6, 10), null, null, 4m, null, "approved");
        var endOfMonth = LeaveRequest.Create(eve.Id, "unpaid", new DateTime(2026, 6, 30), new DateTime(2026, 6, 30), null, null, 8m, null);
        endOfMonth.Update("unpaid", new DateTime(2026, 6, 30), new DateTime(2026, 6, 30), null, null, 8m, null, "approved");
        db.LeaveRequests.AddRange(leave, endOfMonth);
        db.PayItems.Add(PayItem.Create(eve.Id, PayItemKind.Allowance, "Transport", 150m, true, true, 0, new DateOnly(2026, 1, 1), null, "admin"));
        var advance = CashAdvance.Create(eve.Id, 500m, new DateOnly(2026, 4, 2), "Rent", "admin");
        advance.SetRepaymentPerMonth(200m);
        db.CashAdvances.Add(advance);
        var earlier = PayrollRun.Start(2026, 5, eve.Id, new PayrollRunBasis("1", 26m, 8m, 0.5m, 0), DateTime.UtcNow);
        db.PayrollRuns.Add(earlier);
        db.CashAdvanceRepayments.Add(CashAdvanceRepayment.Create(advance.Id, earlier.Id, 321.45m, DateTime.UtcNow));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        var person = Assert.Single(loaded.People);
        Assert.Equal(new DateOnly(1990, 3, 4), person.DateOfBirth);              // decrypted
        Assert.Equal([2600m, 5200m], person.Salaries.Select(s => s.Basic));     // decrypted, oldest first
        Assert.Equal(2, person.Leave.Count);                                     // the leave on the last day of the month is found
        Assert.Equal(321.45m, Assert.Single(person.Advances).Repaid);             // the encrypted repayment, summed in memory
        var result = PayrollRunCalculator.Calculate(person, loaded.Month);
        var days = result.UnpaidDays.ToDictionary(d => d.Date, d => (d.Days, d.DeductionAmount));
        Assert.Equal((0.5m, 50m), days[new DateOnly(2026, 6, 10)]);              // 2600 / 26 x 0.5, the old salary
        Assert.Equal((1m, 200m), days[new DateOnly(2026, 6, 30)]);               // 5200 / 26, the new salary
        Assert.Contains(result.Lines, l => l.Type == "allowance" && l.Amount == 150m);
        var repayment = Assert.Single(result.Lines, l => l.Section == HRNexus.Domain.Payroll.LineSection.OtherDeduction);
        Assert.Equal(178.55m, repayment.Amount);                                 // 500 - 321.45 still owed, below the 200 schedule
    }

    [Fact]
    public async Task A_calculation_can_be_stored_in_a_run_and_read_back()
    {
        await using var db = await FreshAsync();
        var eve = Staff.Create("EVE1", "Eve", "Tan", null, "hash", [], dateOfBirth: new DateTime(1990, 3, 4));
        eve.SetEmployment("citizen", new DateOnly(2024, 1, 1), null);
        var preparer = Staff.Create("PREP1", "Prep", "Aring", null, "hash", []);
        db.Staff.AddRange(eve, preparer);
        db.SalaryProfiles.Add(SalaryProfile.Create(eve.Id, new DateOnly(2024, 1, 1), 3000m, "monthly", null, null, null));
        await db.SaveChangesAsync();
        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);
        var person = Assert.Single(loaded.People);
        var run = PayrollRun.Start(2026, 6, preparer.Id, new PayrollRunBasis("1", loaded.Month.Divisor, loaded.Month.Company.WorkdayHours, loaded.Month.DeductionCapRate, loaded.PolicyVersion), DateTime.UtcNow);
        run.AddLine(PayrollRunLine.Create(person.EmployeeId, person.StaffId, person.Name), preparer.Id, DateTime.UtcNow);

        run.ApplyResult(person.EmployeeId, PayrollRunCalculator.Calculate(person, loaded.Month), preparer.Id, DateTime.UtcNow);
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var line = (await db.PayrollRuns.Include(r => r.Lines).ThenInclude(l => l.Items).AsSplitQuery().SingleAsync()).Lines.Single();
        Assert.True(line.Calculated);
        Assert.Contains(line.Items, i => i.Type == "basic" && i.Amount == 3000m);
    }

    [Fact]
    public async Task Someone_with_no_salary_is_listed_as_not_in_the_run_on_the_real_database()
    {
        await using var db = await FreshAsync();
        var nosal = Staff.Create("NOSAL1", "No", "Salary", null, "hash", []);
        nosal.SetEmployment("citizen", new DateOnly(2024, 1, 1), null);
        db.Staff.Add(nosal);
        await db.SaveChangesAsync();

        var loaded = await PayrollRunInputs.LoadAsync(db, 2026, 6, CancellationToken.None);

        Assert.Empty(loaded.People);
        Assert.Equal("NOSAL1", Assert.Single(loaded.NotInRun).StaffId);
    }
}
