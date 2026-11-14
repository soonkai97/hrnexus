namespace HRNexus.Domain.Entities;

public class Role
{
    public Guid     Id         { get; private set; }
    public string   Name       { get; private set; } = default!;
    /// <summary>The first administrator job: it cannot be deleted, always holds every permission, and its
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

    /// <summary>Renames the job. Stored UPPERCASE. Safe for every job, including the first administrator job:
    /// access is decided by permissions, never by a job's name.</summary>
    public void SetName(string name) => Name = name.Trim().ToUpperInvariant();

    /// <summary>Flags this job as the first administrator job (seed only).</summary>
    public void MarkSystem() => IsSystem = true;
}
