using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HRNexus.Application.Common.Interfaces;
using System.Text.Json;
using WebPush;
using DomainPushSub = HRNexus.Domain.Entities.PushSubscription;

namespace HRNexus.Infrastructure.Services;

public class PushNotificationService(
    IAppDbContext db,
    IConfiguration config,
    ILogger<PushNotificationService> logger) : IPushNotificationService
{
    private readonly string _subject    = config["WebPush:Subject"]    ?? "mailto:admin@example.com";
    private readonly string _publicKey  = config["WebPush:PublicKey"]  ?? string.Empty;
    private readonly string _privateKey = config["WebPush:PrivateKey"] ?? string.Empty;

    public async Task SendAsync(Guid employeeId, string title, string body, string? url = null, CancellationToken ct = default)
    {
        var subs = await db.PushSubscriptions
            .Where(s => s.EmployeeId == employeeId)
            .ToListAsync(ct);
        await SendToSubscriptions(subs, title, body, url, ct);
    }

    public async Task SendToManyAsync(IEnumerable<Guid> employeeIds, string title, string body, string? url = null, CancellationToken ct = default)
    {
        var ids = employeeIds.ToList();
        var subs = await db.PushSubscriptions
            .Where(s => ids.Contains(s.EmployeeId))
            .ToListAsync(ct);
        await SendToSubscriptions(subs, title, body, url, ct);
    }

    private async Task SendToSubscriptions(
        List<DomainPushSub> subs,
        string title, string body, string? url, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_publicKey)) return;  // Push not configured

        // Angular ngsw-worker.js requires the "notification" wrapper to auto-display push messages.
        // "onActionClick" with navigateLastFocusedOrOpen opens the app and navigates to the url.
        var payload = JsonSerializer.Serialize(new
        {
            notification = new
            {
                title,
                body,
                icon  = "icons/icon-192x192.png",
                badge = "icons/icon-96x96.png",
                data  = new
                {
                    onActionClick = new
                    {
                        @default = new { operation = "navigateLastFocusedOrOpen", url = url ?? "/" }
                    }
                }
            }
        });
        var client  = new WebPushClient();
        var vapid   = new VapidDetails(_subject, _publicKey, _privateKey);

        var expiredSubs = new List<DomainPushSub>();

        foreach (var sub in subs)
        {
            try
            {
                var webPushSub = new PushSubscription
                {
                    Endpoint = sub.Endpoint,
                    P256DH   = sub.P256dh,
                    Auth     = sub.Auth,
                };
                await client.SendNotificationAsync(webPushSub, payload, vapid, ct);
            }
            catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                // Subscription expired — mark for removal
                expiredSubs.Add(sub);
                logger.LogInformation("Removing expired push subscription for employee {EmployeeId}", sub.EmployeeId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send push notification to endpoint {Endpoint}", sub.Endpoint);
            }
        }

        if (expiredSubs.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(expiredSubs);
            await db.SaveChangesAsync(ct);
        }
    }
}
