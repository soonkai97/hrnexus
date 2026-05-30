namespace HRNexus.Domain.Entities;

/// <summary>
/// Effective-dated hourly rate for a role. The rate that applies on a given work date is the row
/// with the greatest <see cref="EffectiveOn"/> &lt;= that date. A new row is appended whenever a
/// role's rate changes; existing rows are never mutated, so the rate for any past (or back-dated)
/// shift can be reconstructed as-of its work date instead of using the role's current rate.
/// </summary>
public class RoleRate
{
    public Guid     Id          { get; private set; }
    public string   RoleName    { get; private set; } = default!;
    public decimal  Rate        { get; private set; }
    public DateOnly EffectiveOn { get; private set; }
    public DateTime CreatedAt   { get; private set; }
    public string?  CreatedBy   { get; private set; }

    private RoleRate() { }

    public static RoleRate Create(string roleName, decimal rate, DateOnly effectiveOn, string? createdBy = null) => new()
    {
        Id          = Guid.NewGuid(),
        RoleName    = roleName.Trim().ToUpperInvariant(),
        Rate        = rate,
        EffectiveOn = effectiveOn,
        CreatedAt   = DateTime.UtcNow,
        CreatedBy   = createdBy,
    };
}
