namespace HRNexus.Domain.Entities;

public class Role
{
    public Guid     Id         { get; private set; }
    public string   Name       { get; private set; } = default!;
    public bool     IsSystem   { get; private set; }  // system roles cannot be deleted
    public DateTime CreatedAt  { get; private set; }

    private Role() { }

    public static Role Create(string name, bool isSystem = false) => new()
    {
        Id         = Guid.NewGuid(),
        Name       = name.Trim().ToUpperInvariant(),
        IsSystem   = isSystem,
        CreatedAt  = DateTime.UtcNow,
    };

    /// <summary>Renames the job's primary (English) display name. Stored UPPERCASE to match the
    /// snapshot/auth-matching convention. Never call this for a system role — its Name is the auth key.</summary>
    public void SetName(string name) => Name = name.Trim().ToUpperInvariant();
}
