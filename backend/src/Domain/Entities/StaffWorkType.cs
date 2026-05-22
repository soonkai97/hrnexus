namespace NightclubWorkforce.Domain.Entities;

/// <summary>
/// Admin-managed work type (e.g. VENUECLOCK, TABLERUN) with display metadata and behaviour flags.
/// AutoCalc — venue earnings = units × rate.
/// HasPrivate — private gross/net fields are relevant for this type.
/// </summary>
public sealed class StaffWorkType
{
    public Guid   Id           { get; private set; }
    public string Key          { get; private set; } = string.Empty;   // e.g. "VENUECLOCK"
    public string Label        { get; private set; } = string.Empty;   // e.g. "写钟"
    public bool   AutoCalc     { get; private set; }
    public bool   HasPrivate   { get; private set; }
    public int    DisplayOrder { get; private set; }
    public bool   IsActive     { get; private set; }

    private StaffWorkType() { }

    public static StaffWorkType Create(
        string key,
        string label,
        bool autoCalc,
        bool hasPrivate,
        int displayOrder,
        bool isActive = true)
    {
        return new StaffWorkType
        {
            Id           = Guid.NewGuid(),
            Key          = key.Trim().ToUpperInvariant(),
            Label        = label.Trim(),
            AutoCalc     = autoCalc,
            HasPrivate   = hasPrivate,
            DisplayOrder = displayOrder,
            IsActive     = isActive,
        };
    }

    public void Update(string label, bool autoCalc, bool hasPrivate, int displayOrder, bool isActive)
    {
        Label        = label.Trim();
        AutoCalc     = autoCalc;
        HasPrivate   = hasPrivate;
        DisplayOrder = displayOrder;
        IsActive     = isActive;
    }
}
