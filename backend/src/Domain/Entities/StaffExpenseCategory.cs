namespace NightclubWorkforce.Domain.Entities;

/// <summary>
/// Admin-managed deduction/expense category (e.g. MEAL, TRANSPORT, PENALTY).
/// Used as a lookup for StaffExpense.Category.
/// </summary>
public sealed class StaffExpenseCategory
{
    public Guid   Id           { get; private set; }
    public string Key          { get; private set; } = string.Empty;   // e.g. "MEAL"
    public string Label        { get; private set; } = string.Empty;   // e.g. "Meal Subsidy"
    public int    DisplayOrder { get; private set; }
    public bool   IsActive     { get; private set; }

    private StaffExpenseCategory() { }

    public static StaffExpenseCategory Create(
        string key,
        string label,
        int    displayOrder,
        bool   isActive = true)
    {
        return new StaffExpenseCategory
        {
            Id           = Guid.NewGuid(),
            Key          = key.Trim().ToUpperInvariant(),
            Label        = label.Trim(),
            DisplayOrder = displayOrder,
            IsActive     = isActive,
        };
    }

    public void Update(string label, int displayOrder, bool isActive)
    {
        Label        = label.Trim();
        DisplayOrder = displayOrder;
        IsActive     = isActive;
    }
}
