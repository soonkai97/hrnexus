using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

public record GetReportQuery(
    DateTime       DateFrom,
    DateTime       DateTo,
    List<Guid>?    StaffIds,
    List<string>?  LocationNames,
    List<string>?  Roles
) : IRequest<ReportResponse>;

public record ReportResponse(
    ReportSummary          Summary,
    List<StaffReportRow>   ByStaff,
    List<LocationReportRow> ByLocation,
    List<RoleReportRow>    ByRole,
    List<DateReportRow>    ByDate,
    List<ShiftDetailRow>   Shifts
);

public record ReportSummary(
    int     TotalShifts,
    decimal TotalHours,
    decimal TotalCost,
    int     UniqueStaff,
    int     UniqueLocations,
    decimal AvgHoursPerShift
);

public record StaffReportRow(
    string  StaffId,
    string  Name,
    string  Role,
    int     Shifts,
    decimal Hours,
    decimal Cost,
    decimal AvgHoursPerShift
);

public record LocationReportRow(
    string  LocationName,
    int     Shifts,
    decimal Hours,
    decimal Cost
);

public record RoleReportRow(
    string  Role,
    int     Shifts,
    decimal Hours,
    decimal Cost,
    int     StaffCount
);

public record DateReportRow(
    DateOnly Date,
    int      Shifts,
    decimal  Hours,
    decimal  Cost
);

public record ShiftDetailRow(
    Guid      Id,
    string    StaffId,
    string    StaffName,
    string    Role,
    string    LocationId,
    string    LocationName,
    string    WorkDate,    // yyyy-MM-dd
    string    StartTime,   // HH:mm
    string?   EndTime,     // HH:mm (null = active)
    string?   Notes,
    bool      IsEdited,
    DateTime  ClockedIn,
    DateTime? ClockedOut,
    decimal   Hours,
    decimal   Cost,
    string    Status
);

public class GetReportQueryHandler(IAppDbContext db)
    : IRequestHandler<GetReportQuery, ReportResponse>
{
    public async Task<ReportResponse> Handle(GetReportQuery req, CancellationToken ct)
    {
        var from = DateOnly.FromDateTime(req.DateFrom);
        var to   = DateOnly.FromDateTime(req.DateTo);

        var query = db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.WorkDate >= from && w.WorkDate <= to && w.EndTime != null);

        if (req.StaffIds?.Count > 0)
            query = query.Where(w => req.StaffIds.Contains(w.EmployeeId));

        if (req.LocationNames?.Count > 0)
        {
            var names = req.LocationNames.Select(n => n.ToLowerInvariant()).ToList();
            query = query.Where(w => names.Contains(w.LocationName.ToLower()));
        }

        if (req.Roles?.Count > 0)
        {
            var roles = req.Roles.Select(r => r.ToUpperInvariant()).ToList();
            query = query.Where(w => roles.Contains(w.RoleName));
        }

        var logs = await query.ToListAsync(ct);

        var shifts = logs.Select(w =>
        {
            // Cost estimate = total shift hours × the role's snapshotted hourly rate.
            var raw   = ShiftHours.CalcHours(w.StartTime, w.EndTime!);
            var cost  = Math.Round(raw * w.HourlyRateSnapshot, 2);
            var hours = Math.Round(raw, 2);
            var startDt = w.WorkDate.ToDateTime(TimeOnly.Parse(w.StartTime));
            var endDt   = w.EndTime != null ? w.WorkDate.ToDateTime(TimeOnly.Parse(w.EndTime)) : (DateTime?)null;
            // Handle midnight crossover (e.g. 22:00 → 02:00)
            if (endDt.HasValue && endDt.Value < startDt) endDt = endDt.Value.AddDays(1);
            return new ShiftDetailRow(
                w.Id,
                w.Employee?.StaffId ?? "", w.Employee?.FullName ?? "Unknown",
                w.RoleName,
                w.LocationId, w.LocationName,
                w.WorkDate.ToString("yyyy-MM-dd"), w.StartTime, w.EndTime,
                w.Notes,
                w.IsEdited,
                startDt, endDt, hours, cost, "Complete");
        }).ToList();

        var summary = new ReportSummary(shifts.Count, Math.Round(shifts.Sum(s => s.Hours), 1), Math.Round(shifts.Sum(s => s.Cost), 2), shifts.Select(s => s.StaffId).Distinct().Count(), shifts.Select(s => s.LocationName).Distinct().Count(), shifts.Count > 0 ? Math.Round(shifts.Average(s => s.Hours), 2) : 0m);

        var byStaff    = shifts.GroupBy(s => new { s.StaffId, s.StaffName, s.Role }).Select(g => new StaffReportRow(g.Key.StaffId, g.Key.StaffName, g.Key.Role, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2), g.Count() > 0 ? Math.Round(g.Average(s => s.Hours), 2) : 0m)).OrderByDescending(r => r.Cost).ToList();
        var byLocation = shifts.GroupBy(s => s.LocationName).Select(g => new LocationReportRow(g.Key, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2))).OrderByDescending(r => r.Cost).ToList();
        var byRole     = shifts.GroupBy(s => s.Role).Select(g => new RoleReportRow(g.Key, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2), g.Select(s => s.StaffId).Distinct().Count())).OrderByDescending(r => r.Cost).ToList();
        var byDate     = shifts.GroupBy(s => DateOnly.FromDateTime(s.ClockedIn)).Select(g => new DateReportRow(g.Key, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2))).OrderBy(r => r.Date).ToList();

        return new ReportResponse(summary, byStaff, byLocation, byRole, byDate, shifts);
    }
}
