namespace HRNexus.Domain.Entities;

public class Role
{
    public Guid     Id         { get; private set; }
    public string   Name       { get; private set; } = default!;
    /// <summary>The first administrator role: it cannot be deleted, always holds every permission, and its
    /// permissions cannot be edited. Flagged, never identified by name.</summary>
    public bool     IsSystem   { get; private set; }
    public DateTime CreatedAt  { get; private set; }

    private Role() { }

    public static Role Create(string name, bool isSystem = false) => new()
    {
        Id         = Guid.NewGuid(),
        Name       = name.Trim().ToUpperInvariant(),
        IsSystem   = isSystem,
        CreatedAt  = DateTime.UtcNow,
    };

    /// <summary>Renames the role. Stored UPPERCASE. Safe for every role, including the first administrator role:
    /// access is decided by permissions, never by a role's name.</summary>
    public void SetName(string name) => Name = name.Trim().ToUpperInvariant();

    /// <summary>Flags this role as the first administrator role (seed only).</summary>
    public void MarkSystem() => IsSystem = true;
}
