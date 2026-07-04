using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

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
    decimal AvgHoursPerShift,
    // Labour-cost split by pay model — how much came from fixed-per-shift packages vs hourly work.
    int     FixedShifts,
    decimal FixedCost,
    int     HourlyShifts,
    decimal HourlyCost
);

public record StaffReportRow(
    string  StaffId,
    string  Name,
    string  Role,
    int     Shifts,
    decimal Hours,
    decimal Cost,
    decimal AvgHoursPerShift,
    string? RoleZh = null,
    string? RoleVi = null
);

public record LocationReportRow(
    string  LocationName,
    int     Shifts,
    decimal Hours,
    decimal Cost,
    string? LocationNameZh = null,
    string? LocationNameVi = null
);

public record RoleReportRow(
    string  Role,
    int     Shifts,
    decimal Hours,
    decimal Cost,
    int     StaffCount,
    string? RoleZh = null,
    string? RoleVi = null
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
    string    PayType,     // "hourly" | "fixed"
    string    LocationId,
    string    LocationName,
    string    WorkDate,    // yyyy-MM-dd
    string    StartTime,   // HH:mm
    string?   EndDate,     // yyyy-MM-dd (null = active; may differ from WorkDate for overnight/multi-day)
    string?   EndTime,     // HH:mm (null = active)
    string?   Notes,
    bool      IsEdited,
    DateTime  ClockedIn,
    DateTime? ClockedOut,
    decimal   Hours,
    decimal   Cost,
    string    Status,
    string?   RoleZh = null,
    string?   RoleVi = null,
    string?   LocationNameZh = null,
    string?   LocationNameVi = null
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

        // Localized job + location names for the viewer's language, resolved once from the loaded rows:
        // role by frozen name, per-shift location by exact id, and the by-location grouping (which keys
        // on the frozen LocationName) by name.
        var roleLoc  = await RoleLocalization.BuildAsync(db, logs.Select(w => w.RoleName), ct);
        var locById  = await LocationLocalization.BuildByIdsAsync(db, logs.Select(w => w.LocationId), ct);
        var locByName = await LocationLocalization.BuildByNamesAsync(db, logs.Select(w => w.LocationName), ct);

        var shifts = logs.Select(w =>
        {
            // Cost estimate = total shift hours × the role's snapshotted hourly rate. Uses the shift's
            // recorded end DATE (multi-day aware); legacy rows without one fall back to derive.
            var endDate = w.EndDate ?? (w.EndTime != null ? ShiftHours.DeriveEndDate(w.WorkDate, w.StartTime, w.EndTime) : w.WorkDate);
            var raw   = w.EndTime != null ? ShiftHours.CalcHours(w.WorkDate, w.StartTime, endDate, w.EndTime) : 0m;
            var cost  = Math.Round(ShiftPay.Calc(w.PayTypeSnapshot, raw, w.HourlyRateSnapshot), 2);
            // Shown hours mirror what's billed: hourly shifts floor to 0.5 blocks, fixed keep exact.
            var hours = Math.Round(ShiftHours.BillableHours(w.PayTypeSnapshot, raw), 2);
            var startDt = w.WorkDate.ToDateTime(TimeOnly.Parse(w.StartTime));
            var endDt   = w.EndTime != null ? endDate.ToDateTime(TimeOnly.Parse(w.EndTime)) : (DateTime?)null;
            var (roleZh, roleVi) = roleLoc.For(w.RoleName);
            var (locZh, locVi)   = locById.For(w.LocationId);
            return new ShiftDetailRow(
                w.Id,
                w.Employee?.StaffId ?? "", w.Employee?.FullName ?? "Unknown",
                w.RoleName,
                w.PayTypeSnapshot,
                w.LocationId, w.LocationName,
                w.WorkDate.ToString("yyyy-MM-dd"), w.StartTime, w.EndDate?.ToString("yyyy-MM-dd"), w.EndTime,
                w.Notes,
                w.IsEdited,
                startDt, endDt, hours, cost, "Complete",
                roleZh, roleVi, locZh, locVi);
        }).ToList();

        var fixedRows  = shifts.Where(s => s.PayType == AppConstants.PayType.Fixed).ToList();
        var hourlyRows = shifts.Where(s => s.PayType != AppConstants.PayType.Fixed).ToList();
        var summary = new ReportSummary(
            shifts.Count,
            Math.Round(shifts.Sum(s => s.Hours), 1),
            Math.Round(shifts.Sum(s => s.Cost), 2),
            shifts.Select(s => s.StaffId).Distinct().Count(),
            shifts.Select(s => s.LocationName).Distinct().Count(),
            shifts.Count > 0 ? Math.Round(shifts.Average(s => s.Hours), 2) : 0m,
            fixedRows.Count,  Math.Round(fixedRows.Sum(s => s.Cost), 2),
            hourlyRows.Count, Math.Round(hourlyRows.Sum(s => s.Cost), 2));

        var byStaff    = shifts.GroupBy(s => new { s.StaffId, s.StaffName, s.Role }).Select(g => { var (rZh, rVi) = roleLoc.For(g.Key.Role); return new StaffReportRow(g.Key.StaffId, g.Key.StaffName, g.Key.Role, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2), g.Any() ? Math.Round(g.Average(s => s.Hours), 2) : 0m, rZh, rVi); }).OrderByDescending(r => r.Cost).ToList();
        var byLocation = shifts.GroupBy(s => s.LocationName).Select(g => { var (lZh, lVi) = locByName.For(g.Key); return new LocationReportRow(g.Key, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2), lZh, lVi); }).OrderByDescending(r => r.Cost).ToList();
        var byRole     = shifts.GroupBy(s => s.Role).Select(g => { var (rZh, rVi) = roleLoc.For(g.Key); return new RoleReportRow(g.Key, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2), g.Select(s => s.StaffId).Distinct().Count(), rZh, rVi); }).OrderByDescending(r => r.Cost).ToList();
        var byDate     = shifts.GroupBy(s => DateOnly.FromDateTime(s.ClockedIn)).Select(g => new DateReportRow(g.Key, g.Count(), Math.Round(g.Sum(s => s.Hours), 2), Math.Round(g.Sum(s => s.Cost), 2))).OrderBy(r => r.Date).ToList();

        return new ReportResponse(summary, byStaff, byLocation, byRole, byDate, shifts);
    }
}
