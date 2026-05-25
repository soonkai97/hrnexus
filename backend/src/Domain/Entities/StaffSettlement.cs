using HRNexus.Domain.Common;

namespace HRNexus.Domain.Entities;

/// <summary>
/// Admin-managed settlement status (e.g. SETTLED, PENDING) with PrimeNG severity for UI colouring.
/// Severity values: success | warn | danger | info | secondary | contrast.
/// </summary>
public sealed class StaffSettlement : IHasAudit
{
    public Guid   Id           { get; private set; }
    public string Key          { get; private set; } = string.Empty;   // e.g. "SETTLED"
    public string Label        { get; private set; } = string.Empty;   // e.g. "清 (Settled)"
    public string Severity     { get; private set; } = string.Empty;   // PrimeNG Tag severity
    public int    DisplayOrder { get; private set; }
    public bool   IsActive     { get; private set; }

    // ── Audit (set automatically by AppDbContext) ──────────────────────────────────
    public DateTime  CreatedAt  { get; private set; }
    public string    CreatedBy  { get; private set; } = string.Empty;
    public DateTime? ModifiedAt { get; private set; }
    public string?   ModifiedBy { get; private set; }

    private StaffSettlement() { }

    public static StaffSettlement Create(
        string key,
        string label,
        string severity,
        int displayOrder,
        bool isActive = true)
    {
        return new StaffSettlement
        {
            Id           = Guid.NewGuid(),
            Key          = key.Trim().ToUpperInvariant(),
            Label        = label.Trim(),
            Severity     = severity.Trim().ToLowerInvariant(),
            DisplayOrder = displayOrder,
            IsActive     = isActive,
        };
    }

    public void Update(string label, string severity, int displayOrder, bool isActive)
    {
        Label        = label.Trim();
        Severity     = severity.Trim().ToLowerInvariant();
        DisplayOrder = displayOrder;
        IsActive     = isActive;
    }
}
