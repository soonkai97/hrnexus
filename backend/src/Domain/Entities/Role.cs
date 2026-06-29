namespace HRNexus.Domain.Entities;

public class Role
{
    public string   Name       { get; private set; } = default!;
    /// <summary>Optional Chinese name, shown when the UI language is Chinese (falls back to Name).</summary>
    public string?  NameZh     { get; private set; }
    public bool     IsSystem   { get; private set; }  // system roles cannot be deleted
    public decimal  HourlyRate { get; private set; }
    public DateTime CreatedAt  { get; private set; }

    private Role() { }

    public static Role Create(string name, bool isSystem = false, decimal hourlyRate = 0, string? nameZh = null) => new()
    {
        Name       = name.Trim().ToUpperInvariant(),
        NameZh     = Clean(nameZh),
        IsSystem   = isSystem,
        HourlyRate = hourlyRate,
        CreatedAt  = DateTime.UtcNow,
    };

    public void SetRate(decimal rate) => HourlyRate = rate;
    public void SetNameZh(string? nameZh) => NameZh = Clean(nameZh);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
