namespace NightclubWorkforce.Domain.Entities;

public class Role
{
    public string   Name       { get; private set; } = default!;
    public bool     IsSystem   { get; private set; }  // system roles cannot be deleted
    public decimal  HourlyRate { get; private set; }
    public DateTime CreatedAt  { get; private set; }

    private Role() { }

    public static Role Create(string name, bool isSystem = false, decimal hourlyRate = 0) => new()
    {
        Name       = name.ToLower(),
        IsSystem   = isSystem,
        HourlyRate = hourlyRate,
        CreatedAt  = DateTime.UtcNow,
    };

    public void SetRate(decimal rate) => HourlyRate = rate;
}
