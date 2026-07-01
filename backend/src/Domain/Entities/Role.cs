using HRNexus.Domain.Constants;

namespace HRNexus.Domain.Entities;

public class Role
{
    public Guid     Id         { get; private set; }
    public string   Name       { get; private set; } = default!;
    /// <summary>Optional Chinese name, shown when the UI language is Chinese (falls back to Name).</summary>
    public string?  NameZh     { get; private set; }
    /// <summary>Optional Vietnamese name, shown when the UI language is Vietnamese (falls back to Name).</summary>
    public string?  NameVi     { get; private set; }
    public bool     IsSystem   { get; private set; }  // system roles cannot be deleted
    public decimal  HourlyRate { get; private set; }  // per hour (hourly) OR flat per shift (fixed)
    /// <summary>"hourly" = pay is HourlyRate × hours; "fixed" = pay is HourlyRate flat per shift.</summary>
    public string   PayType    { get; private set; } = AppConstants.PayType.Hourly;
    public DateTime CreatedAt  { get; private set; }

    private Role() { }

    public static Role Create(string name, bool isSystem = false, decimal hourlyRate = 0,
        string? nameZh = null, string? nameVi = null, string payType = AppConstants.PayType.Hourly) => new()
    {
        Id         = Guid.NewGuid(),
        Name       = name.Trim().ToUpperInvariant(),
        NameZh     = Clean(nameZh),
        NameVi     = Clean(nameVi),
        IsSystem   = isSystem,
        HourlyRate = hourlyRate,
        PayType    = NormalizePayType(payType),
        CreatedAt  = DateTime.UtcNow,
    };

    public void SetRate(decimal rate) => HourlyRate = rate;
    public void SetPayType(string payType) => PayType = NormalizePayType(payType);
    public void SetNameZh(string? nameZh) => NameZh = Clean(nameZh);
    public void SetNameVi(string? nameVi) => NameVi = Clean(nameVi);

    private static string NormalizePayType(string? p) =>
        string.Equals(p, AppConstants.PayType.Fixed, StringComparison.OrdinalIgnoreCase)
            ? AppConstants.PayType.Fixed : AppConstants.PayType.Hourly;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
