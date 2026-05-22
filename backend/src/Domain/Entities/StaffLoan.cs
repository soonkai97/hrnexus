using NightclubWorkforce.Domain.Common;

namespace NightclubWorkforce.Domain.Entities;

/// <summary>
/// Records a cash advance (借) or repayment (还) for a staff member.
/// Running balance is calculated in-app from all loan records.
/// </summary>
public sealed class StaffLoan : IHasAudit, ISoftDelete
{
    public Guid     Id        { get; private set; }
    public Guid     StaffId   { get; private set; }
    public DateOnly LoanDate  { get; private set; }
    /// <summary>ADVANCE (借) | REPAYMENT (还)</summary>
    public string   LoanType  { get; private set; } = string.Empty;
    public decimal  Amount    { get; private set; }
    public string?  Purpose   { get; private set; }
    public bool      IsDeleted  { get; private set; }
    public DateTime  CreatedAt  { get; private set; }
    public string    CreatedBy  { get; private set; } = string.Empty;
    public DateTime? ModifiedAt { get; private set; }
    public string?   ModifiedBy { get; private set; }
    public DateTime? DeletedAt  { get; private set; }
    public string?   DeletedBy  { get; private set; }

    public Staff? Staff { get; private set; }

    private StaffLoan() { }

    public static StaffLoan Create(
        Guid staffId,
        DateOnly loanDate,
        string loanType,
        decimal amount,
        string? purpose = null)
    {
        return new StaffLoan
        {
            Id        = Guid.NewGuid(),
            StaffId   = staffId,
            LoanDate  = loanDate,
            LoanType  = loanType.Trim().ToUpperInvariant(),
            Amount    = amount,
            Purpose   = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim(),
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void Delete() { IsDeleted = true; }
}
