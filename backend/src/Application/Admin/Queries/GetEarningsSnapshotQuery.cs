using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

/// <summary>
/// Live per-staff earnings: how much each employee has earned <b>today</b> and <b>this calendar
/// month</b> so far, from completed shifts priced at each shift's snapshotted hourly rate.
/// "Today"/"this month" are anchored to the venue's local date. Still-open (not-yet-ended)
/// shifts are excluded — they're only payable once clocked out.
/// </summary>
public record GetEarningsSnapshotQuery : IRequest<EarningsSnapshotResult>;

public record StaffEarningsRow(
    string  StaffId,
    string  Name,
    decimal EarnedToday,
    decimal EarnedThisMonth);

public record EarningsSnapshotResult(
    string                  Date,            // today's local date, yyyy-MM-dd
    string                  Month,           // current month, yyyy-MM
    decimal                 TotalToday,
    decimal                 TotalThisMonth,
    List<StaffEarningsRow>  Staff);

public class GetEarningsSnapshotQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetEarningsSnapshotQuery, EarningsSnapshotResult>
{
    public async Task<EarningsSnapshotResult> Handle(GetEarningsSnapshotQuery req, CancellationToken ct)
    {
        var today      = DateOnly.FromDateTime(dt.LocalNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Completed shifts in the current month (the most a "today" shift can span back to is monthStart).
        var logs = await db.WorkLogs
            .Where(w => w.WorkDate >= monthStart && w.WorkDate <= today && w.EndTime != null)
            .Include(w => w.Employee)
            .ToListAsync(ct);

        var rows = logs
            .Where(w => w.Employee != null)
            .GroupBy(w => w.EmployeeId)
            .Select(g =>
            {
                decimal earnedMonth = 0m, earnedToday = 0m;
                foreach (var w in g)
                {
                    var endDate = w.EndDate ?? ShiftHours.DeriveEndDate(w.WorkDate, w.StartTime, w.EndTime!);
                    var h = ShiftHours.CalcHours(w.WorkDate, w.StartTime, endDate, w.EndTime!);
                    if (h <= 0) continue;   // guard against bad data
                    var pay = h * w.HourlyRateSnapshot;
                    earnedMonth += pay;
                    if (w.WorkDate == today) earnedToday += pay;
                }
                var emp = g.First().Employee!;
                return new StaffEarningsRow(emp.StaffId, emp.FullName,
                    Math.Round(earnedToday, 2), Math.Round(earnedMonth, 2));
            })
            .Where(r => r.EarnedThisMonth > 0)
            .OrderByDescending(r => r.EarnedToday)
            .ThenByDescending(r => r.EarnedThisMonth)
            .ToList();

        return new EarningsSnapshotResult(
            today.ToString("yyyy-MM-dd"),
            today.ToString("yyyy-MM"),
            Math.Round(rows.Sum(r => r.EarnedToday), 2),
            Math.Round(rows.Sum(r => r.EarnedThisMonth), 2),
            rows);
    }
}
