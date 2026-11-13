namespace HRNexus.Application.Common.Interfaces;

public interface INotificationHub
{
    /// <summary>Push a notification to a specific employee's connected clients.</summary>
    Task SendToUserAsync(Guid employeeId, NotificationPayload payload, CancellationToken ct = default);
}

public record NotificationPayload(
    string  Id,
    string  Title,
    string  Message,
    string  Type,
    string? RelatedId,
    string  CreatedAt);
