namespace NightclubWorkforce.Domain.Entities;

/// <summary>
/// Configurable loan type (e.g. ADVANCE, REPAYMENT, PETTY_CASH).
/// System types (IsSystem=true) cannot be deleted — they underpin the net loan balance formula.
/// IsAdvance=true means cash-out (increases balance); false means cash-in (decreases balance).
/// </summary>
public sealed class StaffLoanType
{
    public Guid   Id           { get; private set; }
    public string Key          { get; private set; } = string.Empty;
    public string Label        { get; private set; } = string.Empty;
    public bool   IsAdvance    { get; private set; }
    public int    DisplayOrder { get; private set; }
    public bool   IsActive     { get; private set; }
    public bool   IsSystem     { get; private set; }

    private StaffLoanType() { }

    public static StaffLoanType Create(
        string key, string label, bool isAdvance, int displayOrder,
        bool isSystem = false, bool isActive = true)
    {
        return new StaffLoanType
        {
            Id           = Guid.NewGuid(),
            Key          = key.Trim().ToUpperInvariant(),
            Label        = label.Trim(),
            IsAdvance    = isAdvance,
            DisplayOrder = displayOrder,
            IsSystem     = isSystem,
            IsActive     = isActive,
        };
    }

    /// <summary>Update mutable fields. IsAdvance and IsSystem are immutable after creation.</summary>
    public void Update(string label, int displayOrder, bool isActive)
    {
        Label        = label.Trim();
        DisplayOrder = displayOrder;
        IsActive     = isActive;
    }
}
