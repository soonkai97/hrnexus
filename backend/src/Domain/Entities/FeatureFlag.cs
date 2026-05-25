namespace HRNexus.Domain.Entities;

public class FeatureFlag
{
    public string   Key       { get; private set; } = string.Empty;
    public bool     IsEnabled { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private FeatureFlag() { }

    public static FeatureFlag Create(string key, bool isEnabled = false) =>
        new() { Key = key, IsEnabled = isEnabled, UpdatedAt = DateTime.UtcNow };

    public void SetEnabled(bool value)
    {
        IsEnabled = value;
        UpdatedAt = DateTime.UtcNow;
    }
}
