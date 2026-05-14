using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.WorkLog.Queries;

/// <summary>
/// Lightweight query: returns the four numbers needed for the dashboard live-stats broadcast.
/// Called after every clock-in/clock-out to push an update over SignalR.
/// </summary>
public record GetLiveStatsQuery : IRequest<LiveStatsDto>;

public class GetLiveStatsQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetLiveStatsQuery, LiveStatsDto>
{
    public async Task<LiveStatsDto> Handle(GetLiveStatsQuery req, CancellationToken ct)
    {
        var localNow = dt.LocalNow;
        var today    = DateOnly.FromDateTime(localNow);

        var todayLogs = await db.WorkLogs
            .Where(w => w.WorkDate == today)
            .ToListAsync(ct);

        var onShiftCount = todayLogs.Count(w => w.EndTime == null);
        var totalStaff   = await db.Staff.CountAsync(e => e.IsActive, ct);

        var tonightHours = 0m;
        var tonightCost  = 0m;
        foreach (var w in todayLogs)
        {
            var endHhMm = w.EndTime ?? $"{localNow.Hour:D2}:{localNow.Minute:D2}";
            var s    = TimeSpan.Parse(w.StartTime);
            var e    = TimeSpan.Parse(endHhMm);
            var diff = e > s ? e - s : e + TimeSpan.FromHours(24) - s;
            var h    = (decimal)diff.TotalHours;
            tonightHours += h;
            tonightCost  += h * w.HourlyRateSnapshot;
        }

        return new LiveStatsDto(
            onShiftCount,
            totalStaff,
            Math.Round(tonightHours, 1),
            Math.Round(tonightCost, 2));
    }
}
