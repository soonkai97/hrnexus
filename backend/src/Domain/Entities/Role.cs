using HRNexus.Domain.Constants;

namespace HRNexus.Domain.Entities;

public class Role
{
    public Guid     Id         { get; private set; }
    public string   Name       { get; private set; } = default!;
    public bool     IsSystem   { get; private set; }  // system roles cannot be deleted
    public decimal  HourlyRate { get; private set; }  // per hour (hourly) OR flat per shift (fixed)
    /// <summary>"hourly" = pay is HourlyRate × hours; "fixed" = pay is HourlyRate flat per shift.</summary>
    public string   PayType    { get; private set; } = AppConstants.PayType.Hourly;
    public DateTime CreatedAt  { get; private set; }

    private Role() { }

    public static Role Create(string name, bool isSystem = false, decimal hourlyRate = 0,
        string payType = AppConstants.PayType.Hourly) => new()
    {
        Id         = Guid.NewGuid(),
        Name       = name.Trim().ToUpperInvariant(),
        IsSystem   = isSystem,
        HourlyRate = hourlyRate,
        PayType    = NormalizePayType(payType),
        CreatedAt  = DateTime.UtcNow,
    };

    /// <summary>Renames the job's primary (English) display name. Stored UPPERCASE to match the
    /// snapshot/auth-matching convention. Never call this for a system role — its Name is the auth key.</summary>
    public void SetName(string name) => Name = name.Trim().ToUpperInvariant();
    public void SetRate(decimal rate) => HourlyRate = rate;
    public void SetPayType(string payType) => PayType = NormalizePayType(payType);

    private static string NormalizePayType(string? p) =>
        string.Equals(p, AppConstants.PayType.Fixed, StringComparison.OrdinalIgnoreCase)
            ? AppConstants.PayType.Fixed : AppConstants.PayType.Hourly;
}
