namespace NightclubWorkforce.Domain.Entities;

/// <summary>In-app message between staff and managers.</summary>
public class StaffMessage
{
    public Guid    Id            { get; private set; }
    public Guid    SenderId      { get; private set; }
    public string  SenderName    { get; private set; } = default!;
    public bool    SenderIsAdmin { get; private set; }
    /// <summary>null = sent to all managers/admins.</summary>
    public Guid?   RecipientId   { get; private set; }
    public string? RecipientName { get; private set; }
    public string  Content       { get; private set; } = default!;
    public DateTime SentAt       { get; private set; }
    public DateTime? ReadAt      { get; private set; }

    private StaffMessage() { }

    public static StaffMessage Create(Guid senderId, string senderName, bool senderIsAdmin,
        Guid? recipientId, string? recipientName, string content) => new()
    {
        Id = Guid.NewGuid(), SenderId = senderId, SenderName = senderName,
        SenderIsAdmin = senderIsAdmin, RecipientId = recipientId,
        RecipientName = recipientName, Content = content, SentAt = DateTime.UtcNow,
    };

    public void MarkRead() => ReadAt ??= DateTime.UtcNow;
}
