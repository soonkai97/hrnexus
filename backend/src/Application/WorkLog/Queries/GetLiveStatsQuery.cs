using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Queries;

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
            // Skip any record with a malformed time string (defensive guard against bad DB data)
            if (!TimeSpan.TryParse(w.StartTime, out var s)) continue;
            var endHhMm = w.EndTime ?? $"{localNow.Hour:D2}:{localNow.Minute:D2}";
            if (!TimeSpan.TryParse(endHhMm, out var e)) continue;
            // Identical times → 0 hours (same guard as CalcHours)
            var diff = s == e ? TimeSpan.Zero : (e > s ? e - s : e + TimeSpan.FromHours(24) - s);
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
