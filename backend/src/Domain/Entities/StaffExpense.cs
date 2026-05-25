using HRNexus.Domain.Common;

namespace HRNexus.Domain.Entities;

/// <summary>
/// Monthly deduction applied to a staff member's payout (uniform, transport, fines, etc.).
/// Multiple expense records can exist per staff member per month.
/// </summary>
public sealed class StaffExpense : IHasAudit, ISoftDelete
{
    public Guid     Id        { get; private set; }
    public Guid     StaffId   { get; private set; }
    public int      Year      { get; private set; }
    public int      Month     { get; private set; }
    public string   Category  { get; private set; } = string.Empty;
    public decimal  Amount    { get; private set; }
    public string?  Notes     { get; private set; }
    public bool      IsDeleted  { get; private set; }
    public DateTime  CreatedAt  { get; private set; }
    public string    CreatedBy  { get; private set; } = string.Empty;
    public DateTime? ModifiedAt { get; private set; }
    public string?   ModifiedBy { get; private set; }
    public DateTime? DeletedAt  { get; private set; }
    public string?   DeletedBy  { get; private set; }

    public Staff? Staff { get; private set; }

    private StaffExpense() { }

    public static StaffExpense Create(
        Guid staffId,
        int year,
        int month,
        string category,
        decimal amount,
        string? notes = null)
    {
        return new StaffExpense
        {
            Id        = Guid.NewGuid(),
            StaffId   = staffId,
            Year      = year,
            Month     = month,
            Category  = category.Trim().ToUpperInvariant(),
            Amount    = amount,
            Notes     = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };
    }

    public void Update(string category, decimal amount, string? notes)
    {
        Category  = category.Trim().ToUpperInvariant();
        Amount    = amount;
        Notes     = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ModifiedAt = DateTime.UtcNow;
    }

    public void Delete()
    {
        IsDeleted = true;
        ModifiedAt = DateTime.UtcNow;
    }
}
