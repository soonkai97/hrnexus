using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

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
    Guid     WorkLogId,
    string   StaffId,
    string   Name,
    string   Role,
    string   LocationName,
    string   StartTime,
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

        // All currently-open shifts (EndTime is null), any date.
        // A shift can start before midnight and still be running the next day,
        // so we must not filter by WorkDate here.
        var activeShifts = await db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.EndTime == null)
            .OrderBy(w => w.StartTime)
            .ToListAsync(ct);

        // Load today's work logs (by venue-local date) for the activity feed and
        // tonight's hours / cost totals (separate from the live "who is on shift" list).
        var todayLogs = await db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.WorkDate == today)
            .OrderByDescending(w => w.StartTime)
            .ToListAsync(ct);

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

        // "Stale" open shifts: still clocked in but WorkDate is NOT today.
        // These are almost certainly staff who forgot to clock out from a previous day.
        var openShiftsCount = activeShifts.Count(w => w.WorkDate != today);

        var stats = new DashboardStats(
            activeShifts.Count,   // everyone currently clocked in, any date
            totalStaff,
            Math.Round(tonightHours, 1),
            Math.Round(tonightCost, 2),
            openShiftsCount);     // only stale (forgot-to-clock-out) entries

        var onShift = activeShifts
            .Where(w => w.Employee != null)
            .Select(w =>
            {
                var clockedInUtc  = LocalShiftToUtc(w.WorkDate, w.StartTime);
                var minutesOnShift = Math.Max(0, (int)(utcNow - clockedInUtc).TotalMinutes);
                return new ActiveStaffDto(
                    w.Id,
                    w.Employee!.StaffId,
                    w.Employee.FullName,
                    w.RoleName,
                    w.LocationName,
                    w.StartTime,
                    clockedInUtc,
                    minutesOnShift);
            })
            .OrderBy(s => s.ClockedInAt)
            .ToList();

        var recent = todayLogs
            .Take(30)
            .Select(w => new RecentEventDto(
                w.Employee?.FullName ?? "Unknown",
                w.RoleName,
                w.LocationName,
                LocalShiftToUtc(w.WorkDate, w.StartTime),
                w.EndTime == null ? "shift-started" : "shift-ended"))
            .ToList();

        return new DashboardResponse(stats, onShift, recent);
    }
}
