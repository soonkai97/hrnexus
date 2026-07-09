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
    /// <summary>Maximum length (in days) a single shift for this job may span — a sanity/typo guard on
    /// clock-out and manual/admin entry. Some jobs run continuously for days; others are always same-day.
    /// Defaults to <see cref="AppConstants.WorkLog.MaxShiftDays"/>.</summary>
    public int      MaxShiftDays { get; private set; } = AppConstants.WorkLog.MaxShiftDays;
    public DateTime CreatedAt  { get; private set; }

    private Role() { }

    public static Role Create(string name, bool isSystem = false, decimal hourlyRate = 0,
        string? nameZh = null, string? nameVi = null, string payType = AppConstants.PayType.Hourly,
        int maxShiftDays = AppConstants.WorkLog.MaxShiftDays) => new()
    {
        Id           = Guid.NewGuid(),
        Name         = name.Trim().ToUpperInvariant(),
        NameZh       = Clean(nameZh),
        NameVi       = Clean(nameVi),
        IsSystem     = isSystem,
        HourlyRate   = hourlyRate,
        PayType      = NormalizePayType(payType),
        MaxShiftDays = ClampDays(maxShiftDays),
        CreatedAt    = DateTime.UtcNow,
    };

    /// <summary>Renames the job's primary (English) display name. Stored UPPERCASE to match the
    /// snapshot/auth-matching convention. Never call this for a system role — its Name is the auth key.</summary>
    public void SetName(string name) => Name = name.Trim().ToUpperInvariant();
    public void SetRate(decimal rate) => HourlyRate = rate;
    public void SetPayType(string payType) => PayType = NormalizePayType(payType);
    public void SetNameZh(string? nameZh) => NameZh = Clean(nameZh);
    public void SetNameVi(string? nameVi) => NameVi = Clean(nameVi);
    public void SetMaxShiftDays(int days) => MaxShiftDays = ClampDays(days);

    private static string NormalizePayType(string? p) =>
        string.Equals(p, AppConstants.PayType.Fixed, StringComparison.OrdinalIgnoreCase)
            ? AppConstants.PayType.Fixed : AppConstants.PayType.Hourly;

    // Keep the per-job cap within the sane 1..ceiling window so it always functions as a typo guard
    // (a job can't be configured to allow an unbounded shift, nor a zero-length one).
    private static int ClampDays(int days) => Math.Clamp(days, 1, AppConstants.WorkLog.MaxShiftDaysCeiling);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
