using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Queries;

public record GetAllLeaveBalancesQuery(int? Year) : IRequest<List<StaffLeaveBalanceDto>>;

public record StaffLeaveBalanceDto(
    Guid   EmployeeId,
    string StaffId,
    string FullName,
    int    Year,
    int    AnnualEntitlement,
    double AnnualUsed,
    double AnnualRemaining,
    int    SickEntitlement,
    double SickUsed,
    double SickRemaining,
    double UnpaidUsed);

public class GetAllLeaveBalancesQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAllLeaveBalancesQuery, List<StaffLeaveBalanceDto>>
{
    public async Task<List<StaffLeaveBalanceDto>> Handle(GetAllLeaveBalancesQuery req, CancellationToken ct)
    {
        var year        = req.Year ?? DateTime.UtcNow.Year;
        var startOfYear = new DateTime(year,     1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfYear   = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);

        // All active staff, sorted by name
        var staff = await db.Staff
            .Where(s => s.IsActive)
            .OrderBy(s => s.FirstName).ThenBy(s => s.LastName)
            .Select(s => new { s.Id, s.StaffId, s.FirstName, s.LastName })
            .ToListAsync(ct);

        if (staff.Count == 0) return [];

        var staffIds = staff.Select(s => s.Id).ToList();

        // Custom entitlements for this year (if none exists, defaults of 14/14 apply)
        var entitlements = await db.LeaveEntitlements
            .Where(e => staffIds.Contains(e.EmployeeId) && e.Year == year)
            .ToDictionaryAsync(e => e.EmployeeId, ct);

        // All approved leave for this year across all staff
        var leaves = await db.LeaveRequests
            .Where(l => staffIds.Contains(l.EmployeeId)
                     && l.Status == "approved"
                     && l.StartDate >= startOfYear
                     && l.StartDate <= endOfYear)
            .Select(l => new { l.EmployeeId, l.LeaveType, l.StartDate, l.EndDate })
            .ToListAsync(ct);

        static double CalcDays(DateTime start, DateTime end) =>
            (end.Date - start.Date).TotalDays + 1;

        var byEmployee = leaves
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return staff.Select(s =>
        {
            var ent       = entitlements.GetValueOrDefault(s.Id);
            var annualEnt = ent?.AnnualDays ?? 14;
            var sickEnt   = ent?.SickDays   ?? 14;

            var emp        = byEmployee.GetValueOrDefault(s.Id);
            var annualUsed = emp?.Where(l => l.LeaveType == "annual").Sum(l => CalcDays(l.StartDate, l.EndDate)) ?? 0;
            var sickUsed   = emp?.Where(l => l.LeaveType == "sick"  ).Sum(l => CalcDays(l.StartDate, l.EndDate)) ?? 0;
            var unpaidUsed = emp?.Where(l => l.LeaveType == "unpaid" ).Sum(l => CalcDays(l.StartDate, l.EndDate)) ?? 0;

            return new StaffLeaveBalanceDto(
                s.Id,
                s.StaffId,
                $"{s.FirstName} {s.LastName}",
                year,
                annualEnt, annualUsed, Math.Max(0, annualEnt - annualUsed),
                sickEnt,   sickUsed,   Math.Max(0, sickEnt   - sickUsed),
                unpaidUsed);
        }).ToList();
    }
}
