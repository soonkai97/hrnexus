using HRNexus.Application.Leave;
using HRNexus.Application.Payroll;
using HRNexus.Domain.Leave;
using HRNexus.Domain.Policy;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// The run calculator joins the input builder to the payroll calculator and returns what a run line stores.
///
/// WHY these tests matter: a later correction compares what was paid with what is due, so each unpaid day must be
/// stored with the amount it really cost, using the salary that was in force on that day. And the warnings a
/// preparer sees must be the calculator's and the builder's together, or a missing date of birth would never be
/// noticed.
/// </summary>
public class PayrollRunCalculatorTests
{
    private static readonly Guid Employee = Guid.NewGuid();

    private static MonthContext Month() =>
        new(2026, 6, 26m, WorkSchedule.Default, new LeaveCatalog(LeaveTypeDefaults.All(), false), 0.5m, []);

    private static PersonSource Person(IReadOnlyList<SalaryRow>? salaries = null, IReadOnlyList<PayrollLeaveRow>? leave = null,
        IReadOnlyList<AdvanceRow>? advances = null, bool noDob = false, decimal? pcb = 0m) =>
        new(Employee, "EMP1", "Eve Tan", noDob ? null : new DateOnly(1990, 1, 1), "citizen", null, null,
            salaries ?? [new SalaryRow(Guid.NewGuid(), new DateOnly(2020, 1, 1), 3000m)], leave ?? [], [], advances ?? [], pcb);

    private static PayrollLeaveRow Unpaid(string from, string? to = null, decimal? hours = 8m) =>
        new(Guid.NewGuid(), "unpaid", DateOnly.Parse(from), DateOnly.Parse(to ?? from), hours, "approved");

    [Fact]
    public void An_unpaid_day_takes_one_twenty_sixth_of_the_basic_off_the_gross()
    {
        var r = PayrollRunCalculator.Calculate(Person(leave: [Unpaid("2026-06-10")]), Month());

        Assert.Equal(3000m - 115.38m, r.Gross);   // 3000 / 26 = 115.3846..., rounded to the cent
        Assert.Contains(r.Lines, l => l.Type == "unpaid-leave" && l.Amount == 115.38m);
    }

    [Fact]
    public void Each_unpaid_day_is_stored_with_the_amount_it_cost()
    {
        var leave = Unpaid("2026-06-10");

        var r = PayrollRunCalculator.Calculate(Person(leave: [leave]), Month());

        var day = Assert.Single(r.UnpaidDays);
        Assert.Equal((new DateOnly(2026, 6, 10), 1m, (Guid?)leave.Id, 115.38m), (day.Date, day.Days, day.LeaveId, day.DeductionAmount));
    }

    [Fact]
    public void A_half_day_costs_half_a_days_pay()
    {
        var r = PayrollRunCalculator.Calculate(Person(leave: [Unpaid("2026-06-10", hours: 4m)]), Month());

        Assert.Equal(57.69m, Assert.Single(r.UnpaidDays).DeductionAmount);   // 3000 / 26 x 0.5 = 57.6923
    }

    /// <summary>WHY: a day before a mid-month raise costs the old rate, a day after it the new one; a later correction needs each day's own amount.</summary>
    [Fact]
    public void An_unpaid_day_is_valued_with_the_salary_in_force_on_that_day()
    {
        var rows = new[]
        {
            new SalaryRow(Guid.NewGuid(), new DateOnly(2020, 1, 1), 2600m),
            new SalaryRow(Guid.NewGuid(), new DateOnly(2026, 6, 16), 5200m),
        };

        var r = PayrollRunCalculator.Calculate(Person(rows, [Unpaid("2026-06-10"), Unpaid("2026-06-22")]), Month());

        var byDate = r.UnpaidDays.ToDictionary(d => d.Date, d => d.DeductionAmount);
        Assert.Equal(100m, byDate[new DateOnly(2026, 6, 10)]);   // 2600 / 26
        Assert.Equal(200m, byDate[new DateOnly(2026, 6, 22)]);   // 5200 / 26
    }

    [Fact]
    public void An_unpaid_day_with_no_salary_in_force_is_stored_at_zero_and_flagged_by_the_calculator()
    {
        var rows = new[] { new SalaryRow(Guid.NewGuid(), new DateOnly(2026, 6, 20), 3000m) };

        var r = PayrollRunCalculator.Calculate(Person(rows, [Unpaid("2026-06-10")]), Month());

        Assert.Equal(0m, Assert.Single(r.UnpaidDays).DeductionAmount);
        Assert.Contains(r.Warnings, w => w.Code == "unpaid-day-outside-employment");
    }

    [Fact]
    public void The_warnings_of_the_input_builder_and_of_the_calculator_are_both_returned()
    {
        var r = PayrollRunCalculator.Calculate(Person(noDob: true, pcb: null), Month());

        Assert.Contains(r.Warnings, w => w.Code == "no-date-of-birth" && w.Blocking);   // from the builder
        Assert.Contains(r.Warnings, w => w.Code == "no-pcb-entered");                    // from the calculator
    }

    /// <summary>
    /// WHY: locking a run writes the repayment from what was really deducted. With a RM1,000 basic the 50% cap
    /// allows RM500, so a RM900 advance repayment is applied as RM500 and the rest is reported as unapplied.
    /// </summary>
    [Fact]
    public void A_cash_advance_larger_than_the_cap_is_applied_partly_and_the_line_names_the_advance()
    {
        var advance = new AdvanceRow(Guid.NewGuid(), 900m, new DateOnly(2026, 5, 2), null, 0m);
        var rows = new[] { new SalaryRow(Guid.NewGuid(), new DateOnly(2020, 1, 1), 1000m) };

        var r = PayrollRunCalculator.Calculate(Person(rows, advances: [advance]), Month());

        var line = Assert.Single(r.Lines, l => l.SourceId == advance.Id);
        Assert.Equal(500m, line.Amount);
        Assert.Equal(400m, r.UnappliedDeductions);
    }

    [Fact]
    public void The_result_carries_the_figures_a_run_line_stores()
    {
        var r = PayrollRunCalculator.Calculate(Person(), Month());

        Assert.Equal((3000m, 3000m), (r.Gross, r.Net));   // no rates in this context, so no contributions
        Assert.Equal(3000m, r.EmployerCost);
        Assert.Contains(r.Lines, l => l.Type == "basic" && l.Amount == 3000m);
    }
}
