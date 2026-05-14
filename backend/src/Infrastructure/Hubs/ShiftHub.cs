using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Infrastructure.Hubs;

[Authorize]
public class ShiftHub : Hub
{
    // Clients connect here; the server pushes via IShiftHub / INotificationHub adapters below
}

public class NotificationHubAdapter(IHubContext<ShiftHub> ctx) : INotificationHub
{
    public async Task SendToUserAsync(Guid employeeId, NotificationPayload payload,
        CancellationToken ct = default)
    {
        await ctx.Clients.User(employeeId.ToString()).SendAsync("Notification", new
        {
            id        = payload.Id,
            title     = payload.Title,
            message   = payload.Message,
            type      = payload.Type,
            relatedId = payload.RelatedId,
            createdAt = payload.CreatedAt,
        }, ct);
    }

    public async Task BroadcastLiveStatsAsync(LiveStatsDto stats, CancellationToken ct = default)
    {
        await ctx.Clients.All.SendAsync("LiveStatsUpdate", new
        {
            onShiftCount        = stats.OnShiftCount,
            totalStaff          = stats.TotalStaff,
            tonightHours        = stats.TonightHours,
            estimatedLabourCost = stats.EstimatedLabourCost,
        }, ct);
    }
}

