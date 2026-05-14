namespace NightclubWorkforce.Application.Common.Interfaces;

public interface IPushNotificationService
{
    Task SendAsync(Guid employeeId, string title, string body, string? url = null, CancellationToken ct = default);
    Task SendToManyAsync(IEnumerable<Guid> employeeIds, string title, string body, string? url = null, CancellationToken ct = default);
}
