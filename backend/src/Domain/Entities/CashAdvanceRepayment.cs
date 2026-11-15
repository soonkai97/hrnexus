namespace HRNexus.Domain.Entities;

/// <summary>
/// What a locked payroll run really deducted for one cash advance (the amount applied after the deduction cap and
/// the net-pay floor, never the amount that was scheduled). An advance is settled exactly when its repayments reach
/// its amount. Reopening a run deletes that run's repayments. Nothing here moves money.
/// </summary>
public class CashAdvanceRepayment
{
    public Guid     Id            { get; private set; }
    public Guid     CashAdvanceId { get; private set; }
    public Guid     RunId         { get; private set; }
    public decimal  Amount        { get; private set; }
    public DateTime CreatedAt     { get; private set; }

    private CashAdvanceRepayment() { }

    public static CashAdvanceRepayment Create(Guid cashAdvanceId, Guid runId, decimal amount, DateTime at)
    {
        if (amount <= 0m) throw new ArgumentException("A repayment must be more than zero.");
        return new() { Id = Guid.NewGuid(), CashAdvanceId = cashAdvanceId, RunId = runId, Amount = amount, CreatedAt = at };
    }
}
