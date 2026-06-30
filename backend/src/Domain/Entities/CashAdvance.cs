namespace HRNexus.Domain.Entities;

/// <summary>
/// A salary advance the venue gave a staff member before payday. The system only RECORDS it
/// (so the owner remembers to deduct it from the next salary) — it never moves money.
/// Admin-recorded: there is no staff request/approval flow, so a freshly recorded advance is
/// immediately <c>outstanding</c> and becomes <c>settled</c> once it's been deducted.
/// </summary>
public class CashAdvance
{
    public const string Outstanding = "outstanding";
    public const string Settled     = "settled";

    public Guid      Id          { get; private set; }
    public Guid      EmployeeId  { get; private set; }
    public decimal   Amount      { get; private set; }
    public DateOnly  AdvanceDate { get; private set; }   // the day the cash was handed over
    public string?   Reason      { get; private set; }
    public string    Status      { get; private set; } = default!;
    public DateTime? SettledAt   { get; private set; }
    public DateTime  CreatedAt   { get; private set; }
    public string    CreatedBy   { get; private set; } = string.Empty;  // admin who recorded it

    public Staff Employee { get; private set; } = default!;

    private CashAdvance() { }

    public static CashAdvance Create(Guid employeeId, decimal amount, DateOnly advanceDate, string? reason, string createdBy)
    {
        if (amount <= 0) throw new ArgumentException("Cash advance amount must be greater than zero.");
        return new()
        {
            Id          = Guid.NewGuid(),
            EmployeeId  = employeeId,
            Amount      = amount,
            AdvanceDate = advanceDate,
            Reason      = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Status      = Outstanding,
            CreatedAt   = DateTime.UtcNow,
            CreatedBy   = createdBy,
        };
    }

    /// <summary>Mark the advance as deducted from salary (settled).</summary>
    public void MarkSettled()
    {
        Status    = Settled;
        SettledAt = DateTime.UtcNow;
    }

    /// <summary>Undo a settle — return the advance to outstanding.</summary>
    public void Reopen()
    {
        Status    = Outstanding;
        SettledAt = null;
    }
}
