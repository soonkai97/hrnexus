namespace HRNexus.Domain.Entities;

public class Announcement
{
    public Guid   Id            { get; private set; }
    public string Title         { get; private set; } = string.Empty;
    public string Body          { get; private set; } = string.Empty;
    /// <summary>Comma-separated roles e.g. "bartender,manager" or "*" for all</summary>
    public string TargetRoles   { get; private set; } = "*";
    public bool   IsActive      { get; private set; } = true;
    public Guid   CreatedById   { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTime  CreatedAt  { get; private set; }
    public DateTime? ExpiresAt  { get; private set; }

    // Read receipts
    public ICollection<AnnouncementRead> Reads { get; private set; } = new List<AnnouncementRead>();

    private Announcement() { }

    public static Announcement Create(string title, string body, string targetRoles,
        Guid createdById, string createdByName, DateTime? expiresAt = null)
    {
        return new Announcement
        {
            Id            = Guid.NewGuid(),
            Title         = title.Trim(),
            Body          = body.Trim(),
            TargetRoles   = string.IsNullOrWhiteSpace(targetRoles) ? "*" : targetRoles.Trim(),
            CreatedById   = createdById,
            CreatedByName = createdByName,
            CreatedAt     = DateTime.UtcNow,
            ExpiresAt     = expiresAt,
            IsActive      = true,
        };
    }

    public void Update(string title, string body, string targetRoles, DateTime? expiresAt)
    {
        Title       = title.Trim();
        Body        = body.Trim();
        TargetRoles = string.IsNullOrWhiteSpace(targetRoles) ? "*" : targetRoles.Trim();
        ExpiresAt   = expiresAt;
    }
}
