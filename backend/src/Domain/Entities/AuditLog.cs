namespace HRNexus.Domain.Entities;

public class AuditLog
{
    public Guid     Id              { get; private set; }
    public string   EntityType      { get; private set; } = string.Empty;  // e.g. "ClockLog", "Staff", "LeaveRequest"
    public string   EntityId        { get; private set; } = string.Empty;
    public string   Action          { get; private set; } = string.Empty;  // e.g. "Updated", "Approved", "Created"
    public Guid?    PerformedById   { get; private set; }
    public string   PerformedByName { get; private set; } = string.Empty;
    public string?  Changes         { get; private set; }  // JSON string of before/after
    public DateTime Timestamp       { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(string entityType, string entityId, string action,
        Guid? performedById, string performedByName, string? changes = null) =>
        new()
        {
            Id              = Guid.NewGuid(),
            EntityType      = entityType,
            EntityId        = entityId,
            Action          = action,
            PerformedById   = performedById,
            PerformedByName = performedByName,
            Changes         = changes,
            Timestamp       = DateTime.UtcNow,
        };
}
