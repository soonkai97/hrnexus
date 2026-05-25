namespace HRNexus.Domain.Entities;

public class OvertimeRule
{
    public Guid    Id             { get; private set; }
    /// <summary>Role name this rule applies to. Use "*" to match all roles.</summary>
    public string  Role           { get; private set; } = default!;
    /// <summary>Hours worked after which this multiplier kicks in (e.g. 8.0).</summary>
    public decimal ThresholdHours { get; private set; }
    /// <summary>Pay rate multiplier applied beyond the threshold (e.g. 1.5).</summary>
    public decimal Multiplier     { get; private set; }
    public bool    IsActive       { get; private set; }
    public DateTime CreatedAt     { get; private set; }

    private OvertimeRule() { }

    public static OvertimeRule Create(string role, decimal thresholdHours, decimal multiplier) => new()
    {
        Id             = Guid.NewGuid(),
        Role           = role.Trim().ToLowerInvariant(),
        ThresholdHours = thresholdHours,
        Multiplier     = multiplier,
        IsActive       = true,
        CreatedAt      = DateTime.UtcNow,
    };

    public void Update(string role, decimal thresholdHours, decimal multiplier, bool isActive)
    {
        Role           = role.Trim().ToLowerInvariant();
        ThresholdHours = thresholdHours;
        Multiplier     = multiplier;
        IsActive       = isActive;
    }
}
