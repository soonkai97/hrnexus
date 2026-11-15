using System.Globalization;
using HRNexus.Application.Leave;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Payroll;
using HRNexus.Domain.Policy;

namespace HRNexus.Application.Payroll;

/// <summary>One row of a person's pay history (the monthly basic from <see cref="EffectiveFrom"/>).</summary>
public sealed record SalaryRow(Guid Id, DateOnly EffectiveFrom, decimal Basic);

/// <summary>The part of a leave request payroll needs (dates without a time zone, the stored hours, the status; the times are only used when hours were never stored).</summary>
public sealed record PayrollLeaveRow(Guid Id, string Type, DateOnly Start, DateOnly End, decimal? Hours, string Status,
    string? StartTime = null, string? EndTime = null);

/// <summary>An outstanding cash advance: what was lent, the monthly schedule if any, and what earlier runs already took back.</summary>
public sealed record AdvanceRow(Guid Id, decimal Amount, DateOnly AdvanceDate, decimal? RepaymentPerMonth, decimal Repaid);

/// <summary>Everything the database says about one person that a month's calculation needs, with nothing decrypted left to do.</summary>
public sealed record PersonSource(
    Guid EmployeeId,
    string StaffId,
    string Name,
    DateOnly? DateOfBirth,
    string? CitizenshipStatus,
    DateOnly? EmploymentStart,
    DateOnly? EmploymentEnd,
    IReadOnlyList<SalaryRow> Salaries,
    IReadOnlyList<PayrollLeaveRow> Leave,
    IReadOnlyList<PayItem> PayItems,
    IReadOnlyList<AdvanceRow> Advances,
    decimal? Pcb);

/// <summary>The settings every person in one month's run is calculated with.</summary>
public sealed record MonthContext(
    int Year,
    int Month,
    decimal Divisor,
    WorkSchedule Company,
    LeaveCatalog Catalog,
    decimal DeductionCapRate,
    IReadOnlyList<RateRule> Rates)
{
    public DateOnly First => new(Year, Month, 1);
    public DateOnly Last  => new(Year, Month, DateTime.DaysInMonth(Year, Month));
}

/// <summary>An unpaid-leave day before its cost is known (the run calculator prices it).</summary>
public sealed record UnpaidDayDraft(DateOnly Date, decimal Days, Guid LeaveId);

public sealed record BuiltInput(PayrollInput Input, IReadOnlyList<UnpaidDayDraft> UnpaidDays, IReadOnlyList<PayrollWarning> Warnings);

/// <summary>
/// Turns one person's data into the payroll calculator's input for one month. Pure: no database, no clock.
/// Payroll never names a leave type; whether leave costs pay is what the company's leave catalogue says.
/// </summary>
public static class PayrollInputBuilder
{
    /// <summary>Age used for rate selection when the date of birth is missing; the blocking warning stops the run from being approved on it.</summary>
    private const int AssumedAge = 30;

    /// <summary>Cash advance repayments are applied before every other deduction.</summary>
    private const int AdvancePriority = int.MinValue;

    public static BuiltInput Build(PersonSource p, MonthContext c)
    {
        var warnings = new List<PayrollWarning>();
        var age = AgeAtMonthEnd(p.DateOfBirth, c.Last);
        if (age is null)
            warnings.Add(new PayrollWarning("no-date-of-birth", "The person's date of birth is not set, so their age was assumed for the statutory rates.", Blocking: true));

        if (p.EmploymentStart is null)
            warnings.Add(new PayrollWarning("no-employment-start", "The person has no employment start date, so they are paid for the whole month.", Blocking: false));

        var unpaid = UnpaidDays(p, c, warnings);

        var earnings = new List<EarningInput>();
        var deductions = new List<DeductionInput>();
        // Cash advance repayments first, in a fixed order, then pay-item deductions by priority: when pay cannot cover
        // everything the outcome must not depend on the order the database returned its rows in.
        foreach (var a in p.Advances.OrderBy(a => a.AdvanceDate).ThenBy(a => a.Id))
        {
            var owed = a.Amount - a.Repaid;
            var scheduled = Math.Min(owed, a.RepaymentPerMonth ?? owed);
            if (a.AdvanceDate > c.Last || scheduled <= 0m) continue;
            deductions.Add(new DeductionInput(
                $"Cash advance repayment ({a.AdvanceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})", scheduled, AdvancePriority, a.Id));
        }
        foreach (var item in p.PayItems.Where(i => i.AppliesInMonth(c.Year, c.Month)).OrderBy(i => i.Priority).ThenBy(i => i.From).ThenBy(i => i.Id))
        {
            if (item.Kind == PayItemKind.Allowance)
                earnings.Add(new EarningInput("allowance", item.Reason, item.Amount, item.CountsForEpf, item.CountsForSocsoEis, item.Id));
            else
                deductions.Add(new DeductionInput(item.Reason, item.Amount, item.Priority, item.Id));
        }

        var input = new PayrollInput(
            c.Year, c.Month, age ?? AssumedAge, p.CitizenshipStatus, p.EmploymentStart, p.EmploymentEnd,
            Segments(p.Salaries, c),
            unpaid.Select(d => new UnpaidLeaveDay(d.Date, d.Days)).ToList(),
            c.Divisor, earnings, deductions, c.DeductionCapRate, p.Pcb, c.Rates);
        return new BuiltInput(input, unpaid, warnings);
    }

    private static int? AgeAtMonthEnd(DateOnly? dob, DateOnly monthEnd)
    {
        if (dob is not { } d) return null;
        var age = monthEnd.Year - d.Year;
        if (monthEnd < d.AddYears(age)) age--;
        return age;
    }

    /// <summary>One segment per salary row, cut to the month: each row applies from its effective date until the next row takes over.</summary>
    private static List<SalarySegment> Segments(IReadOnlyList<SalaryRow> salaries, MonthContext c)
    {
        var rows = salaries.OrderBy(s => s.EffectiveFrom).ToList();
        var segments = new List<SalarySegment>();
        for (var i = 0; i < rows.Count; i++)
        {
            var from = rows[i].EffectiveFrom > c.First ? rows[i].EffectiveFrom : c.First;
            var until = i + 1 < rows.Count ? rows[i + 1].EffectiveFrom.AddDays(-1) : DateOnly.MaxValue;
            var to = until < c.Last ? until : c.Last;
            if (from <= to) segments.Add(new SalarySegment(from, to, rows[i].Basic, rows[i].Id));
        }
        return segments;
    }

    private static List<UnpaidDayDraft> UnpaidDays(PersonSource p, MonthContext c, List<PayrollWarning> warnings)
    {
        var days = new List<UnpaidDayDraft>();
        foreach (var leave in p.Leave)
        {
            var approved = leave.Status.Equals(AppConstants.Leave.Approved, StringComparison.OrdinalIgnoreCase);
            var pending = leave.Status.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase);
            if ((!approved && !pending) || leave.End < c.First || leave.Start > c.Last) continue;

            var type = c.Catalog.Find(leave.Type);
            if (type is null)
            {
                if (approved)
                    warnings.Add(new PayrollWarning("leave-type-unknown",
                        $"Approved leave starting {leave.Start:yyyy-MM-dd} is of a type the company no longer lists, so whether it costs pay cannot be known.", Blocking: true));
                continue;
            }
            if (!type.DeductsPay) continue;
            if (pending)
            {
                warnings.Add(new PayrollWarning("pending-unpaid-leave",
                    $"Leave starting {leave.Start:yyyy-MM-dd} that would cost pay is still pending, so nothing was deducted for it.", Blocking: false));
                continue;
            }

            var schedule = type.ScheduleFor(c.Company);
            if (leave.Start == leave.End)
            {
                if (!schedule.IsWorkingDay(leave.Start.DayOfWeek)) continue;
                var fraction = FractionOfDay(leave, schedule, warnings);
                if (fraction > 0m) days.Add(new UnpaidDayDraft(leave.Start, fraction, leave.Id));
                continue;
            }
            for (var d = leave.Start > c.First ? leave.Start : c.First; d <= leave.End && d <= c.Last; d = d.AddDays(1))
                if (schedule.IsWorkingDay(d.DayOfWeek)) days.Add(new UnpaidDayDraft(d, 1m, leave.Id));
        }
        return OneDayAtMost(days, warnings);
    }

    /// <summary>A date costs one day of pay at most, whatever the data says (two leaves should never cover the same day, but old data or a race could).</summary>
    private static List<UnpaidDayDraft> OneDayAtMost(List<UnpaidDayDraft> days, List<PayrollWarning> warnings)
    {
        var used = new Dictionary<DateOnly, decimal>();
        var kept = new List<UnpaidDayDraft>();
        var warned = new HashSet<DateOnly>();
        foreach (var d in days)
        {
            var room = 1m - used.GetValueOrDefault(d.Date);
            var take = Math.Min(d.Days, room);
            if (take < d.Days && warned.Add(d.Date))
                warnings.Add(new PayrollWarning("unpaid-day-overlap",
                    $"More than one leave covers {d.Date:yyyy-MM-dd}, so it was charged as one day at most.", Blocking: false));
            if (take <= 0m) continue;
            used[d.Date] = used.GetValueOrDefault(d.Date) + take;
            kept.Add(d with { Days = take });
        }
        return kept;
    }

    /// <summary>A day booked before hours were stored: its hours come from its start and end times, by the same rule the leave module uses. Unreadable times count as a full day.</summary>
    private static decimal? HoursFromTimes(PayrollLeaveRow leave, WorkSchedule schedule)
    {
        if (leave.StartTime is null || leave.EndTime is null) return null;
        try
        {
            var day = leave.Start.ToDateTime(TimeOnly.MinValue);
            return LeaveHoursCalculator.Calculate(day, day, leave.StartTime, leave.EndTime, schedule);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A single day of leave: the stored hours over the current working day (4 of 8 hours is half a day), never more than a day.</summary>
    private static decimal FractionOfDay(PayrollLeaveRow leave, WorkSchedule schedule, List<PayrollWarning> warnings)
    {
        var workdayHours = schedule.WorkdayHours;
        var stored = leave.Hours ?? HoursFromTimes(leave, schedule);
        if (stored is not { } hours || workdayHours <= 0m) return 1m;
        var fraction = Math.Round(hours / workdayHours, 4);
        if (fraction <= 1m) return fraction;
        warnings.Add(new PayrollWarning("leave-hours-exceed-day",
            $"Leave on {leave.Start:yyyy-MM-dd} has more hours than a working day now has, so it was counted as one day.", Blocking: false));
        return 1m;
    }
}
