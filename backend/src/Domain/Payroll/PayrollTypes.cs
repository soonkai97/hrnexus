
namespace HRNexus.Domain.Payroll;

/// <summary>One day-range of a person's month with a monthly basic (built by the caller from the effective-dated salary profiles).</summary>
public sealed record SalarySegment(DateOnly From, DateOnly To, decimal Basic, Guid? ProfileId = null);

/// <summary>An unpaid day of leave. <see cref="Days"/> is an exact decimal fraction (4 hours of an 8-hour day is 0.5).</summary>
public sealed record UnpaidLeaveDay(DateOnly Date, decimal Days);

/// <summary>An allowance, overtime or other earning, entered by hand with a reason.</summary>
public sealed record EarningInput(string Type, string Reason, decimal Amount, bool CountsForEpf = true, bool CountsForSocsoEis = true);

/// <summary>A deduction other than statutory contributions and tax (for example a cash-advance repayment).</summary>
public sealed record DeductionInput(string Reason, decimal Amount, int Priority);

public sealed record RateBandRule(decimal WageFrom, decimal WageTo, decimal EmployeeAmount, decimal EmployerAmount);

/// <summary>A rate-table row as the calculator sees it: plain data, no database types.</summary>
public sealed record RateRule(
    Guid Id,
    string Scheme,
    string CitizenshipClass,
    int AgeFrom,
    int? AgeTo,
    DateOnly EffectiveFrom,
    RateKind Kind,
    decimal? WageCeiling,
    decimal EmployeeRate,
    decimal EmployerRate,
    EmployerRateMode EmployerRateMode,
    decimal? EmployerRateThreshold,
    decimal? EmployerRateAbove,
    RoundingRule Rounding,
    decimal? Amount,
    bool Active,
    bool Verified,
    IReadOnlyList<RateBandRule> Bands);

/// <summary>Everything one person's month needs. The calculator reads nothing else: no clock, no database.</summary>
public sealed record PayrollInput(
    int Year,
    int Month,
    int AgeAtMonthEnd,
    string? CitizenshipClass,
    DateOnly? EmploymentStart,
    DateOnly? EmploymentEnd,
    IReadOnlyList<SalarySegment> Segments,
    IReadOnlyList<UnpaidLeaveDay> UnpaidLeave,
    decimal Divisor,
    IReadOnlyList<EarningInput> Earnings,
    IReadOnlyList<DeductionInput> Deductions,
    decimal DeductionCapRate,
    decimal? PcbAmount,
    IReadOnlyList<RateRule> Rates);

public enum LineSection
{
    Earning,
    GrossDeduction,
    EmployeeStatutory,
    Tax,
    OtherDeduction,
    EmployerContribution,
}

/// <summary>One line of a payslip. <see cref="Amount"/> is never negative; the section says which way it counts.</summary>
public sealed record PayLine(string Type, LineSection Section, string Description, decimal Amount, Guid? RateRuleId = null);

public sealed record PayrollWarning(string Code, string Message, bool Blocking);

/// <summary>What is needed to reproduce the figures months later.</summary>
public sealed record PayrollBasis(
    string CalculatorVersion,
    decimal Divisor,
    int DaysInMonth,
    int EmployedDays,
    int AgeAtMonthEnd,
    IReadOnlyList<Guid> RateRuleIds,
    IReadOnlyList<SalarySegment> Segments);

public sealed record PayrollResult(
    IReadOnlyList<PayLine> Lines,
    decimal ProratedBasic,
    decimal UnpaidLeaveDeduction,
    decimal Gross,
    decimal Net,
    decimal EmployerCost,
    decimal UnappliedDeductions,
    PayrollBasis Basis,
    IReadOnlyList<PayrollWarning> Warnings)
{
    public bool HasBlockingWarning => Warnings.Any(w => w.Blocking);
}
