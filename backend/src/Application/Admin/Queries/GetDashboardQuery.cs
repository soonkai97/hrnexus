using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Queries;

public record GetDashboardQuery : IRequest<DashboardResponse>;

public record DashboardResponse(
    DashboardStats         Stats,
    List<ActiveStaffDto>   CurrentlyOnShift,
    List<RecentEventDto>   RecentActivity);

public record DashboardStats(
    int     OnShiftCount,
    int     TotalStaff,
    decimal TonightHours,
    decimal TonightCost,
    int     OpenShiftsCount);

public record ActiveStaffDto(
    string   StaffId,
    string   Name,
    string   Role,
    string   LocationName,
    DateTime ClockedInAt,
    int      MinutesOnShift);

public record RecentEventDto(
    string   StaffName,
    string   Role,
    string   LocationName,
    DateTime Timestamp,
    string   EventType);

public class GetDashboardQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetDashboardQuery, DashboardResponse>
{
    public async Task<DashboardResponse> Handle(GetDashboardQuery req, CancellationToken ct)
    {
        var tz        = dt.VenueTimeZone;
        var localNow  = dt.LocalNow;
        var today     = DateOnly.FromDateTime(localNow);  // "today" in venue local time
        var utcNow    = dt.UtcNow;

        // Load today's work logs (by venue-local date) with employee info
        var todayLogs = await db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.WorkDate == today)
            .OrderByDescending(w => w.StartTime)
            .ToListAsync(ct);

        // Active shifts: EndTime is null
        var activeShifts = todayLogs.Where(w => w.EndTime == null).ToList();

        // Helper: convert WorkDate + HH:mm local string → UTC DateTime
        DateTime LocalShiftToUtc(DateOnly workDate, string hhMm)
        {
            var localDt = workDate.ToDateTime(TimeOnly.Parse(hhMm));          // Kind=Unspecified = venue local
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified), tz);
        }

        // Tonight totals — include active shifts using current local time as effective end
        var tonightHours = 0m;
        var tonightCost  = 0m;
        foreach (var w in todayLogs)
        {
            var endHhMm = w.EndTime
                       ?? $"{localNow.Hour:D2}:{localNow.Minute:D2}";   // active: use now

            var s    = TimeSpan.Parse(w.StartTime);
            var e    = TimeSpan.Parse(endHhMm);
            var diff = e > s ? e - s : e + TimeSpan.FromHours(24) - s;  // handle overnight
            var h    = (decimal)diff.TotalHours;
            tonightHours += h;
            tonightCost  += h * w.HourlyRateSnapshot;
        }

        var totalStaff = await db.Staff.CountAsync(e => e.IsActive, ct);

        // Open shifts across all time
        var openShiftsCount = await db.WorkLogs.CountAsync(w => w.EndTime == null, ct);

        var stats = new DashboardStats(
            activeShifts.Count,
            totalStaff,
            Math.Round(tonightHours, 1),
            Math.Round(tonightCost, 2),
            openShiftsCount);

        var onShift = activeShifts
            .Where(w => w.Employee != null)
            .Select(w =>
            {
                var clockedInUtc  = LocalShiftToUtc(w.WorkDate, w.StartTime);
                var minutesOnShift = Math.Max(0, (int)(utcNow - clockedInUtc).TotalMinutes);
                return new ActiveStaffDto(
                    w.Employee!.StaffId,
                    w.Employee.FullName,
                    w.Employee.RolesDisplay,
                    w.LocationName,
                    clockedInUtc,
                    minutesOnShift);
            })
            .OrderBy(s => s.ClockedInAt)
            .ToList();

        var recent = todayLogs
            .Take(30)
            .Select(w => new RecentEventDto(
                w.Employee?.FullName     ?? "Unknown",
                w.Employee?.RolesDisplay ?? "",
                w.LocationName,
                LocalShiftToUtc(w.WorkDate, w.StartTime),
                w.EndTime == null ? "shift-started" : "shift-ended"))
            .ToList();

        return new DashboardResponse(stats, onShift, recent);
    }
}
