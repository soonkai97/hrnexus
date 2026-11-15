using HRNexus.Domain.Constants;

namespace HRNexus.Domain.Entities;

public static class PayItemKind
{
    public const string Allowance = "allowance";
    public const string Deduction = "deduction";
}

/// <summary>
/// A recurring allowance or deduction on one person's pay (a transport allowance, a parking fee), in force from
/// <see cref="From"/> until <see cref="To"/> (open-ended when null). The amount is encrypted at rest.
/// </summary>
public class PayItem
{
    public Guid      Id                { get; private set; }
    public Guid      EmployeeId        { get; private set; }
    public string    Kind              { get; private set; } = default!;
    public string    Reason            { get; private set; } = default!;
    public decimal   Amount            { get; private set; }
    /// <summary>Allowances only: whether the amount is part of the EPF wage base.</summary>
    public bool      CountsForEpf      { get; private set; }
    /// <summary>Allowances only: whether the amount is part of the SOCSO and EIS wage base.</summary>
    public bool      CountsForSocsoEis { get; private set; }
    /// <summary>Deductions only: the lowest number is applied first when pay cannot cover them all.</summary>
    public int       Priority          { get; private set; }
    public DateOnly  From              { get; private set; }
    public DateOnly? To                { get; private set; }
    public DateTime  CreatedAt         { get; private set; }
    public string?   CreatedBy         { get; private set; }

    private PayItem() { }

    public static PayItem Create(Guid employeeId, string kind, string reason, decimal amount, bool countsForEpf,
        bool countsForSocsoEis, int priority, DateOnly from, DateOnly? to, string? createdBy)
    {
        if (kind is not (PayItemKind.Allowance or PayItemKind.Deduction))
            throw new ArgumentException("A pay item is an allowance or a deduction.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A pay item needs a reason.");
        if (reason.Trim().Length > 200) throw new ArgumentException("The reason can be at most 200 characters.");
        if (amount <= 0m) throw new ArgumentException("The amount must be more than zero.");
        if (amount > AppConstants.Payroll.MaxMonthlySalary) throw new ArgumentException("The amount is too large.");
        if (kind == PayItemKind.Deduction && priority is < 0 or > 1000)
            throw new ArgumentException("A deduction priority must be between 0 and 1000 (the lowest number is applied first).");
        if (to is { } end && end < from) throw new ArgumentException("A pay item cannot end before it starts.");
        var allowance = kind == PayItemKind.Allowance;
        return new PayItem
        {
            Id                = Guid.NewGuid(),
            EmployeeId        = employeeId,
            Kind              = kind,
            Reason            = reason.Trim(),
            Amount            = amount,
            CountsForEpf      = allowance && countsForEpf,
            CountsForSocsoEis = allowance && countsForSocsoEis,
            Priority          = allowance ? 0 : priority,
            From              = from,
            To                = to,
            CreatedAt         = DateTime.UtcNow,
            CreatedBy         = createdBy,
        };
    }

    /// <summary>Sets the last day the item applies (an allowance that stops, a deduction that is paid off). It cannot end before it starts.</summary>
    public void End(DateOnly to)
    {
        if (to < From) throw new ArgumentException("A pay item cannot end before it starts.");
        To = to;
    }

    /// <summary>True when any day of the given month falls inside the item's validity.</summary>
    public bool AppliesInMonth(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last  = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return From <= last && (To is null || To >= first);
    }
}
