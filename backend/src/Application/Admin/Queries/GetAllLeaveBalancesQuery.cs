using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Application.Policy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Queries;

public record GetAllLeaveBalancesQuery(int? Year) : IRequest<List<StaffLeaveBalanceDto>>;

public record StaffLeaveBalanceDto(
    Guid    EmployeeId,
    string  StaffId,
    string  FullName,
    int     Year,
    decimal AnnualEntitlement,
    decimal AnnualUsed,        // annual + emergency combined
    decimal AnnualRemaining,
    decimal EmergencyUsed,     // emergency portion within AnnualUsed
    decimal CarryOverHours,    // hours carried over from the previous year
    decimal SickEntitlement,
    decimal SickUsed,
    decimal SickRemaining,
    decimal UnpaidUsed);

public class GetAllLeaveBalancesQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetAllLeaveBalancesQuery, List<StaffLeaveBalanceDto>>
{
    public async Task<List<StaffLeaveBalanceDto>> Handle(GetAllLeaveBalancesQuery req, CancellationToken ct)
    {
        var year        = req.Year ?? dt.LocalNow.Year;
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

        var entitlements = await db.LeaveEntitlements
            .Where(e => staffIds.Contains(e.EmployeeId) && e.Year == year)
            .ToDictionaryAsync(e => e.EmployeeId, ct);

        var policy = await CompanyPolicyReader.GetAsync(db, ct);
        var leaves = await db.LeaveRequests
            .Where(l => staffIds.Contains(l.EmployeeId)
                     && l.Status != "rejected"
                     && l.Status != "cancelled"
                     && l.StartDate <= endOfYear
                     && l.EndDate   >= startOfYear)
            .Select(l => new { l.EmployeeId, l.LeaveType, l.StartDate, l.EndDate, l.Hours })
            .ToListAsync(ct);
        // A leave across New Year is charged to each year by the working days that fall in it.
        leaves = leaves
            .Select(l => new { l.EmployeeId, l.LeaveType, l.StartDate, l.EndDate,
                Hours = (decimal?)LeaveHoursCalculator.HoursInYear(l.StartDate, l.EndDate, l.Hours, startOfYear.Year, policy.Schedule) })
            .ToList();

        var byEmployee = leaves
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return staff.Select(s =>
        {
            var ent       = entitlements.GetValueOrDefault(s.Id);
            var annualEnt = ent?.TotalAnnualHours ?? 0m;   // base + carry-over
            var sickEnt   = ent?.SickHours     ?? 0m;
            var carryOver = ent?.CarryOverHours ?? 0m;

            var emp        = byEmployee.GetValueOrDefault(s.Id);
            // Annual + emergency both draw from the annual pool
            var annualUsed    = emp?.Where(l => l.LeaveType == "annual" || l.LeaveType == "emergency").Sum(l => l.Hours ?? 0m) ?? 0m;
            var emergencyUsed = emp?.Where(l => l.LeaveType == "emergency").Sum(l => l.Hours ?? 0m) ?? 0m;
            var sickUsed      = emp?.Where(l => l.LeaveType == "sick"  ).Sum(l => l.Hours ?? 0m) ?? 0m;
            var unpaidUsed    = emp?.Where(l => l.LeaveType == "unpaid" ).Sum(l => l.Hours ?? 0m) ?? 0m;

            return new StaffLeaveBalanceDto(
                s.Id,
                s.StaffId,
                $"{s.FirstName} {s.LastName}",
                year,
                annualEnt, annualUsed, Math.Max(0, annualEnt - annualUsed),
                emergencyUsed, carryOver,
                sickEnt,   sickUsed,   Math.Max(0, sickEnt   - sickUsed),
                unpaidUsed);
        }).ToList();
    }
}
