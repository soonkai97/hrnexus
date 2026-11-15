using HRNexus.Application.Leave;
using HRNexus.Application.Payroll;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Leave;
using HRNexus.Domain.Policy;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Turns what the database knows about one person into the calculator's input for one month.
///
/// WHY these tests matter: the calculator is exact, so every error that matters happens before it, in the
/// inputs. A leave day counted twice, a salary raise applied a month early, a cash advance deducted after it
/// was repaid, or an unpaid type found by its name instead of by what the company says it does would each
/// change someone's pay and look completely authoritative on the payslip.
/// </summary>
public class PayrollInputBuilderTests
{
    private static readonly Guid Employee = Guid.NewGuid();

    private static LeaveTypeDefinition Type(string key, bool deductsPay = false, bool calendarDays = false) =>
        LeaveTypeDefinition.Create(new LeaveTypeSpec(key, key, "info", 10, true, LeavePools.None, null, null, false, null, false,
            deductsPay, false, calendarDays, false));

    private static LeaveCatalog Catalog(params LeaveTypeDefinition[] types) => new(types, isSaved: true);

    /// <summary>June 2026: the 1st is a Monday, so weekends fall on the 6th/7th, 13th/14th, 20th/21st, 27th/28th.</summary>
    private static MonthContext Month(LeaveCatalog? catalog = null, int year = 2026, int month = 6, decimal divisor = 26m,
        WorkSchedule? company = null) =>
        new(year, month, divisor, company ?? WorkSchedule.Default, catalog ?? new LeaveCatalog(LeaveTypeDefaults.All(), false),
            0.5m, []);

    private static PersonSource Person(
        IReadOnlyList<SalaryRow>? salaries = null, IReadOnlyList<PayrollLeaveRow>? leave = null,
        IReadOnlyList<PayItem>? items = null, IReadOnlyList<AdvanceRow>? advances = null,
        DateOnly? dob = null, string? klass = "citizen", DateOnly? start = null, DateOnly? end = null, decimal? pcb = null,
        bool noDob = false) =>
        new(Employee, "EMP1", "Eve Tan", noDob ? null : dob ?? new DateOnly(1990, 1, 1), klass, start, end,
            salaries ?? [new SalaryRow(Guid.NewGuid(), new DateOnly(2020, 1, 1), 3000m)], leave ?? [], items ?? [], advances ?? [], pcb);

    private static PayrollLeaveRow Leave(string type, string from, string? to = null, decimal? hours = 8m, string status = "approved") =>
        new(Guid.NewGuid(), type, DateOnly.Parse(from), DateOnly.Parse(to ?? from), hours, status);

    private static BuiltInput Build(PersonSource p, MonthContext? c = null) => PayrollInputBuilder.Build(p, c ?? Month());

    private static decimal DaysOn(BuiltInput b, string date) => b.UnpaidDays.Where(d => d.Date == DateOnly.Parse(date)).Sum(d => d.Days);

    private static bool Has(BuiltInput b, string code, bool? blocking = null) =>
        b.Warnings.Any(w => w.Code == code && (blocking is null || w.Blocking == blocking));

    // ── who and when ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_month_divisor_class_employment_dates_cap_and_pcb_pass_through_to_the_calculator()
    {
        var c = Month(divisor: 30m);

        var input = Build(Person(klass: "permanent-resident", start: new DateOnly(2025, 3, 1), end: new DateOnly(2026, 6, 20), pcb: 120m), c).Input;

        Assert.Equal((2026, 6), (input.Year, input.Month));
        Assert.Equal(30m, input.Divisor);
        Assert.Equal("permanent-resident", input.CitizenshipClass);
        Assert.Equal(new DateOnly(2025, 3, 1), input.EmploymentStart);
        Assert.Equal(new DateOnly(2026, 6, 20), input.EmploymentEnd);
        Assert.Equal(0.5m, input.DeductionCapRate);
        Assert.Equal(120m, input.PcbAmount);
    }

    [Theory]
    [InlineData("1990-06-15", 36)]   // birthday inside the month, before month end
    [InlineData("1990-06-30", 36)]   // birthday on the last day of the month counts
    [InlineData("1990-07-01", 35)]   // the day after month end does not
    [InlineData("1966-06-30", 60)]
    public void Age_is_the_age_reached_by_the_end_of_the_month(string dob, int age) =>
        Assert.Equal(age, Build(Person(dob: DateOnly.Parse(dob))).Input.AgeAtMonthEnd);

    [Fact]
    public void A_person_with_no_date_of_birth_gets_a_blocking_warning_instead_of_a_silent_guess()
    {
        var b = Build(Person(noDob: true));

        Assert.True(Has(b, "no-date-of-birth", blocking: true));
    }

    // ── salary segments ──────────────────────────────────────────────────────────

    [Fact]
    public void One_salary_row_from_before_the_month_covers_the_whole_month()
    {
        var id = Guid.NewGuid();

        var segs = Build(Person([new SalaryRow(id, new DateOnly(2024, 1, 1), 3000m)])).Input.Segments;

        var s = Assert.Single(segs);
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 3000m, (Guid?)id), (s.From, s.To, s.Basic, s.ProfileId));
    }

    /// <summary>WHY: a raise dated the 16th is paid from the 16th. Applying it from the 1st would overpay half a month.</summary>
    [Fact]
    public void A_raise_in_the_middle_of_the_month_splits_it_into_two_segments_at_the_effective_date()
    {
        var rows = new[]
        {
            new SalaryRow(Guid.NewGuid(), new DateOnly(2024, 1, 1), 3000m),
            new SalaryRow(Guid.NewGuid(), new DateOnly(2026, 6, 16), 3500m),
        };

        var segs = Build(Person(rows)).Input.Segments.OrderBy(s => s.From).ToList();

        Assert.Equal(2, segs.Count);
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), 3000m), (segs[0].From, segs[0].To, segs[0].Basic));
        Assert.Equal((new DateOnly(2026, 6, 16), new DateOnly(2026, 6, 30), 3500m), (segs[1].From, segs[1].To, segs[1].Basic));
    }

    [Fact]
    public void Rows_from_earlier_months_do_not_leak_in_and_a_row_after_the_month_is_ignored()
    {
        var rows = new[]
        {
            new SalaryRow(Guid.NewGuid(), new DateOnly(2023, 1, 1), 2000m),
            new SalaryRow(Guid.NewGuid(), new DateOnly(2025, 1, 1), 2500m),
            new SalaryRow(Guid.NewGuid(), new DateOnly(2026, 7, 1), 9999m),   // a raise next month
        };

        var s = Assert.Single(Build(Person(rows)).Input.Segments);

        Assert.Equal(2500m, s.Basic);
    }

    [Fact]
    public void A_salary_that_starts_part_way_through_the_month_leaves_the_earlier_days_uncovered_for_the_calculator_to_report()
    {
        var rows = new[] { new SalaryRow(Guid.NewGuid(), new DateOnly(2026, 6, 11), 3000m) };

        var s = Assert.Single(Build(Person(rows)).Input.Segments);

        Assert.Equal(new DateOnly(2026, 6, 11), s.From);
    }

    // ── unpaid leave ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_full_day_of_approved_unpaid_leave_is_one_unpaid_day()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-06-10")]));

        Assert.Equal(1m, DaysOn(b, "2026-06-10"));
        Assert.Equal(1m, b.Input.UnpaidLeave.Sum(d => d.Days));
    }

    [Theory]
    [InlineData(4, "0.5")]
    [InlineData(2, "0.25")]
    [InlineData(2.5, "0.3125")]
    [InlineData(3, "0.375")]
    public void A_partly_taken_single_day_is_the_fraction_of_the_working_day(double hours, string days) =>
        Assert.Equal(decimal.Parse(days), DaysOn(Build(Person(leave: [Leave("unpaid", "2026-06-10", hours: (decimal)hours)])), "2026-06-10"));

    [Fact]
    public void A_single_day_from_before_hours_were_stored_counts_as_a_full_day()
    {
        Assert.Equal(1m, DaysOn(Build(Person(leave: [Leave("unpaid", "2026-06-10", hours: null)])), "2026-06-10"));
    }

    /// <summary>WHY: the company lengthened its working day after the leave was booked; one leave day must never cost more than one day's pay.</summary>
    [Fact]
    public void Hours_above_the_current_working_day_are_capped_at_one_day_with_a_note()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-06-10", hours: 12m)]));

        Assert.Equal(1m, DaysOn(b, "2026-06-10"));
        Assert.True(Has(b, "leave-hours-exceed-day", blocking: false));
    }

    [Fact]
    public void A_week_of_leave_counts_the_working_days_and_skips_the_weekend()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-06-08", "2026-06-14")]));   // Mon 8th to Sun 14th

        Assert.Equal(5m, b.Input.UnpaidLeave.Sum(d => d.Days));
        Assert.Equal(0m, DaysOn(b, "2026-06-13"));
        Assert.Equal(0m, DaysOn(b, "2026-06-14"));
    }

    [Fact]
    public void Several_days_of_leave_count_whole_days_whatever_hours_were_stored()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-06-08", "2026-06-10", hours: 24m)]));

        Assert.Equal(3m, b.Input.UnpaidLeave.Sum(d => d.Days));
    }

    [Fact]
    public void A_company_that_works_saturdays_pays_less_for_them_when_they_are_unpaid()
    {
        var sixDays = WorkSchedule.FromIsoDays([1, 2, 3, 4, 5, 6], 8m);

        var b = Build(Person(leave: [Leave("unpaid", "2026-06-08", "2026-06-14")]), Month(company: sixDays));

        Assert.Equal(6m, b.Input.UnpaidLeave.Sum(d => d.Days));
    }

    /// <summary>WHY: a calendar-day type (counted straight through, weekends included) must be charged the same way payroll charges it as the leave module counted it.</summary>
    [Fact]
    public void A_calendar_day_type_that_deducts_pay_counts_every_day_including_the_weekend()
    {
        var cat = Catalog(Type("long-absence", deductsPay: true, calendarDays: true));

        var b = Build(Person(leave: [Leave("long-absence", "2026-06-08", "2026-06-14")]), Month(cat));

        Assert.Equal(7m, b.Input.UnpaidLeave.Sum(d => d.Days));
    }

    [Fact]
    public void Leave_that_runs_over_the_month_edge_deducts_only_the_days_inside_the_month()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-05-27", "2026-06-02")]));   // Wed 27 May to Tue 2 Jun

        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2)], b.UnpaidDays.Select(d => d.Date).OrderBy(d => d));
        Assert.Equal(2m, b.Input.UnpaidLeave.Sum(d => d.Days));
    }

    [Fact]
    public void Leave_that_ends_before_the_month_or_starts_after_it_deducts_nothing()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-05-18", "2026-05-22"), Leave("unpaid", "2026-07-01", "2026-07-03")]));

        Assert.Empty(b.Input.UnpaidLeave);
    }

    /// <summary>WHY: pay is deducted for approved leave only. A pending request has not been decided, so it costs nothing yet; the preparer is told.</summary>
    [Fact]
    public void Pending_leave_deducts_nothing_but_is_flagged_for_the_preparer()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-06-10", status: "pending")]));

        Assert.Empty(b.Input.UnpaidLeave);
        Assert.True(Has(b, "pending-unpaid-leave", blocking: false));
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    public void Rejected_and_cancelled_leave_deduct_nothing_and_raise_no_flag(string status)
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-06-10", status: status)]));

        Assert.Empty(b.Input.UnpaidLeave);
        Assert.False(Has(b, "pending-unpaid-leave"));
    }

    [Fact]
    public void Pending_leave_of_a_type_that_does_not_deduct_pay_raises_no_flag()
    {
        var b = Build(Person(leave: [Leave("annual", "2026-06-10", status: "pending")]));

        Assert.False(Has(b, "pending-unpaid-leave"));
    }

    /// <summary>
    /// WHY: payroll never names a leave type. Whether leave costs pay is what the company's catalogue says, so a
    /// company that makes "annual" unpaid deducts it, and one that makes "unpaid" a paid type does not.
    /// </summary>
    [Fact]
    public void Which_leave_costs_pay_is_decided_by_the_catalogue_not_by_the_name_of_the_type()
    {
        var cat = Catalog(Type("annual", deductsPay: true), Type("unpaid", deductsPay: false));

        var b = Build(Person(leave: [Leave("annual", "2026-06-10"), Leave("unpaid", "2026-06-11")]), Month(cat));

        Assert.Equal(1m, DaysOn(b, "2026-06-10"));
        Assert.Equal(0m, DaysOn(b, "2026-06-11"));
    }

    [Fact]
    public void Leave_of_a_type_the_catalogue_no_longer_has_is_a_blocking_warning_because_the_cost_cannot_be_known()
    {
        var b = Build(Person(leave: [Leave("retired-type", "2026-06-10")]));

        Assert.True(Has(b, "leave-type-unknown", blocking: true));
        Assert.Empty(b.Input.UnpaidLeave);
    }

    [Fact]
    public void Leave_type_keys_match_whatever_the_letter_case()
    {
        Assert.Equal(1m, DaysOn(Build(Person(leave: [Leave("UNPAID", "2026-06-10")])), "2026-06-10"));
    }

    [Fact]
    public void A_single_day_on_a_non_working_day_deducts_nothing()
    {
        var b = Build(Person(leave: [Leave("unpaid", "2026-06-13")]));   // a Saturday

        Assert.Empty(b.Input.UnpaidLeave);
    }

    [Fact]
    public void Each_unpaid_day_remembers_which_leave_it_came_from()
    {
        var leave = Leave("unpaid", "2026-06-08", "2026-06-09");

        var b = Build(Person(leave: [leave]));

        Assert.All(b.UnpaidDays, d => Assert.Equal(leave.Id, d.LeaveId));
    }

    // ── pay items ────────────────────────────────────────────────────────────────

    private static PayItem Item(string kind, decimal amount, string from = "2026-01-01", string? to = null, bool epf = true, bool socso = true, int priority = 0) =>
        PayItem.Create(Employee, kind, "Item", amount, epf, socso, priority, DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), "admin");

    [Fact]
    public void An_allowance_in_force_becomes_an_earning_with_its_contribution_flags_and_source()
    {
        var item = Item(PayItemKind.Allowance, 150m, epf: true, socso: false);

        var e = Assert.Single(Build(Person(items: [item])).Input.Earnings);

        Assert.Equal(("allowance", "Item", 150m, true, false, (Guid?)item.Id), (e.Type, e.Reason, e.Amount, e.CountsForEpf, e.CountsForSocsoEis, e.SourceId));
    }

    [Fact]
    public void An_allowance_is_paid_in_full_in_the_month_it_starts_and_the_month_it_ends()
    {
        var starts = Item(PayItemKind.Allowance, 100m, from: "2026-06-20");
        var ends = Item(PayItemKind.Allowance, 200m, from: "2026-01-01", to: "2026-06-05");

        Assert.Equal([100m, 200m], Build(Person(items: [starts, ends])).Input.Earnings.Select(e => e.Amount).OrderBy(a => a));
    }

    [Fact]
    public void An_allowance_that_has_ended_or_not_started_is_left_out()
    {
        var ended = Item(PayItemKind.Allowance, 100m, to: "2026-05-31");
        var future = Item(PayItemKind.Allowance, 100m, from: "2026-07-01");

        Assert.Empty(Build(Person(items: [ended, future])).Input.Earnings);
    }

    [Fact]
    public void A_deduction_item_becomes_a_deduction_with_its_priority_and_source()
    {
        var item = Item(PayItemKind.Deduction, 50m, priority: 3);

        var d = Assert.Single(Build(Person(items: [item])).Input.Deductions);

        Assert.Equal(("Item", 50m, 3, (Guid?)item.Id), (d.Reason, d.Amount, d.Priority, d.SourceId));
    }

    // ── cash advances ────────────────────────────────────────────────────────────

    private static AdvanceRow Advance(decimal amount = 500m, string date = "2026-05-02", decimal? perMonth = null, decimal repaid = 0m) =>
        new(Guid.NewGuid(), amount, DateOnly.Parse(date), perMonth, repaid);

    [Fact]
    public void An_advance_with_no_schedule_is_deducted_in_full_in_the_next_run()
    {
        var a = Advance(500m);

        var d = Assert.Single(Build(Person(advances: [a])).Input.Deductions);

        Assert.Equal((500m, (Guid?)a.Id, "Cash advance repayment (02/05/2026)"), (d.Amount, d.SourceId, d.Reason));
    }

    [Fact]
    public void A_scheduled_advance_deducts_the_monthly_amount_and_never_more_than_is_still_owed()
    {
        var a = Advance(500m, perMonth: 200m, repaid: 400m);   // 100 still owed

        Assert.Equal(100m, Assert.Single(Build(Person(advances: [a])).Input.Deductions).Amount);
    }

    [Fact]
    public void What_has_already_been_repaid_is_taken_off_what_is_owed()
    {
        var a = Advance(500m, repaid: 125.50m);

        Assert.Equal(374.50m, Assert.Single(Build(Person(advances: [a])).Input.Deductions).Amount);
    }

    [Fact]
    public void A_fully_repaid_advance_deducts_nothing()
    {
        Assert.Empty(Build(Person(advances: [Advance(500m, repaid: 500m)])).Input.Deductions);
    }

    [Fact]
    public void An_advance_handed_over_after_the_month_is_not_deducted_in_it()
    {
        Assert.Empty(Build(Person(advances: [Advance(date: "2026-07-01")])).Input.Deductions);
    }

    /// <summary>WHY: when pay cannot cover everything, the advance the venue is owed comes first.</summary>
    [Fact]
    public void Cash_advance_repayments_are_applied_before_any_other_deduction()
    {
        var parking = Item(PayItemKind.Deduction, 50m, priority: 0);
        var a = Advance(500m);

        var deductions = Build(Person(items: [parking], advances: [a])).Input.Deductions;

        var advance = Assert.Single(deductions, d => d.SourceId == a.Id);
        var other = Assert.Single(deductions, d => d.SourceId == parking.Id);
        Assert.True(advance.Priority < other.Priority);
    }
}
