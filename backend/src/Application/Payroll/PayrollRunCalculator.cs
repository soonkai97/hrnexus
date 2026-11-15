using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;

namespace HRNexus.Application.Payroll;

/// <summary>
/// Calculates one person's line of a payroll run: builds the input from what the database holds, runs the payroll
/// calculator, and returns what a run line stores (figures, payslip lines, the builder's and the calculator's
/// warnings together, and the unpaid days each with the amount it cost). Pure.
/// </summary>
public static class PayrollRunCalculator
{
    public static PayrollLineResult Calculate(PersonSource person, MonthContext month)
    {
        var built = PayrollInputBuilder.Build(person, month);
        var result = PayrollCalculator.Calculate(built.Input);

        var days = built.UnpaidDays
            .Select(d => new PayrollRunUnpaidDayInput(d.Date, d.Days, d.LeaveId, CostOfDay(d, built.Input)))
            .ToList();
        var warnings = built.Warnings.Concat(result.Warnings).ToList();

        return new PayrollLineResult(result.Gross, result.Net, result.EmployerCost, result.UnappliedDeductions, result.Lines, warnings, days);
    }

    /// <summary>The day's share of the salary in force that day (basic / divisor x days, to the cent); zero when the person was not employed or had no salary that day.</summary>
    private static decimal CostOfDay(UnpaidDayDraft day, PayrollInput input)
    {
        if (day.Date < (input.EmploymentStart ?? DateOnly.MinValue) || day.Date > (input.EmploymentEnd ?? DateOnly.MaxValue)) return 0m;
        var segment = input.Segments.FirstOrDefault(s => s.From <= day.Date && day.Date <= s.To);
        return segment is null ? 0m : MoneyRounding.Round(segment.Basic / input.Divisor * day.Days, RoundingRule.NearestCent);
    }
}
