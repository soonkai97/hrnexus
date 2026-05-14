namespace NightclubWorkforce.Application.Common.Interfaces;

public interface INotificationHub
{
    /// <summary>Push a notification to a specific employee's connected clients.</summary>
    Task SendToUserAsync(Guid employeeId, NotificationPayload payload, CancellationToken ct = default);

    /// <summary>Broadcast live dashboard stats to all connected managers/admins.</summary>
    Task BroadcastLiveStatsAsync(LiveStatsDto stats, CancellationToken ct = default);
}

public record NotificationPayload(
    string  Id,
    string  Title,
    string  Message,
    string  Type,
    string? RelatedId,
    string  CreatedAt);

public record LiveStatsDto(
    int     OnShiftCount,
    int     TotalStaff,
    decimal TonightHours,
    decimal EstimatedLabourCost);
