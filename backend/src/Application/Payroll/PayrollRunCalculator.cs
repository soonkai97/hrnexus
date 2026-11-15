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

        var days = PriceDays(built.UnpaidDays, built.Input, result.UnpaidLeaveDeduction);
        var warnings = built.Warnings.Concat(result.Warnings).ToList();

        return new PayrollLineResult(result.Gross, result.Net, result.EmployerCost, result.UnappliedDeductions, result.Lines, warnings, days);
    }

    /// <summary>
    /// Shares the unpaid-leave deduction that was really taken between the days, each in proportion to what the salary
    /// in force that day says it costs (basic / divisor x days). The shares add up to the deduction to the cent: when
    /// the deduction was capped at the prorated basic they are scaled down, and the last cent of rounding goes to the
    /// last priced day. A later correction refunds per day, so the days must never total more than was taken.
    /// </summary>
    private static List<PayrollRunUnpaidDayInput> PriceDays(IReadOnlyList<UnpaidDayDraft> days, PayrollInput input, decimal taken)
    {
        var raw = days.Select(d => RawCost(d, input)).ToList();
        var total = raw.Sum();
        var amounts = raw.Select(r => total > 0m ? MoneyRounding.Round(taken * r / total, RoundingRule.NearestCent) : 0m).ToList();
        var drift = taken - amounts.Sum();
        var last = raw.FindLastIndex(r => r > 0m);
        if (drift != 0m && last >= 0) amounts[last] += drift;
        return days.Select((d, i) => new PayrollRunUnpaidDayInput(d.Date, d.Days, d.LeaveId, amounts[i])).ToList();
    }

    /// <summary>What the day costs before rounding and capping; zero when the person was not employed or had no salary that day.</summary>
    private static decimal RawCost(UnpaidDayDraft day, PayrollInput input)
    {
        if (day.Date < (input.EmploymentStart ?? DateOnly.MinValue) || day.Date > (input.EmploymentEnd ?? DateOnly.MaxValue)) return 0m;
        var segment = input.Segments.FirstOrDefault(s => s.From <= day.Date && day.Date <= s.To);
        return segment is null ? 0m : segment.Basic / input.Divisor * day.Days;
    }
}
