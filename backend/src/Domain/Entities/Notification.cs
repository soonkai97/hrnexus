namespace HRNexus.Domain.Entities;

/// <summary>In-app notification for an employee.</summary>
public class Notification
{
    public Guid    Id         { get; private set; }
    public Guid    EmployeeId { get; private set; }
    public string  Title      { get; private set; } = default!;
    public string  Message    { get; private set; } = default!;
    /// <summary>leave_approved | leave_rejected | leave_pending | password_reset | message | announcement | stale_shift | stale_shift_admin</summary>
    public string  Type       { get; private set; } = default!;
    public bool    IsRead     { get; private set; }
    public DateTime CreatedAt { get; private set; }
    /// <summary>Optional ID of the related entity (e.g. LeaveRequest.Id).</summary>
    public string? RelatedId  { get; private set; }

    public Staff Employee { get; private set; } = default!;

    private Notification() { }

    public static Notification Create(Guid employeeId, string title, string message,
        string type, string? relatedId = null) =>
        new()
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId,
            Title = title, Message = message, Type = type,
            IsRead = false, CreatedAt = DateTime.UtcNow,
            RelatedId = relatedId,
        };

    public void MarkRead() => IsRead = true;
}
