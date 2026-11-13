using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Queries;

public record GetLeaveBalanceQuery(Guid EmployeeId, int? Year) : IRequest<LeaveBalanceDto>;

public record LeaveBalanceDto(
    int     Year,
    decimal AnnualEntitlement,
    decimal AnnualUsed,        // annual + emergency combined (both draw from the annual pool)
    decimal AnnualRemaining,
    decimal EmergencyUsed,     // emergency portion within AnnualUsed, shown separately for clarity
    decimal CarryOverHours,    // hours carried over from the previous year
    decimal SickEntitlement,
    decimal SickUsed,
    decimal SickRemaining,
    decimal UnpaidUsed);

public class GetLeaveBalanceQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetLeaveBalanceQuery, LeaveBalanceDto>
{
    public async Task<LeaveBalanceDto> Handle(GetLeaveBalanceQuery req, CancellationToken ct)
    {
        var year = req.Year ?? dt.LocalNow.Year;

        var ent = await db.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.Year == year, ct);

        var annualEnt   = ent?.AnnualHours   ?? 0m;
        var sickEnt     = ent?.SickHours     ?? 0m;
        var carryOver   = ent?.CarryOverHours ?? 0m;

        var startOfYear = new DateTime(year, 1, 1,   0, 0, 0, DateTimeKind.Utc);
        var endOfYear   = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var countedLeaves = await db.LeaveRequests
            .Where(l => l.EmployeeId == req.EmployeeId
                     && l.Status != "rejected"
                     && l.Status != "cancelled"
                     && l.StartDate <= endOfYear
                     && l.EndDate   >= startOfYear)
            .Select(l => new { l.LeaveType, l.StartDate, l.EndDate, l.Hours })
            .ToListAsync(ct);
        // A leave across New Year is charged to each year by the working days that fall in it.
        var inYear = countedLeaves
            .Select(l => new { l.LeaveType, Hours = LeaveHoursCalculator.HoursInYear(l.StartDate, l.EndDate, l.Hours, year) })
            .ToList();

        // Annual + emergency both count against the annual pool
        var annualUsed    = inYear.Where(l => l.LeaveType == "annual" || l.LeaveType == "emergency").Sum(l => l.Hours);
        var emergencyUsed = inYear.Where(l => l.LeaveType == "emergency").Sum(l => l.Hours);
        var sickUsed      = inYear.Where(l => l.LeaveType == "sick"  ).Sum(l => l.Hours);
        var unpaidUsed    = inYear.Where(l => l.LeaveType == "unpaid" ).Sum(l => l.Hours);

        return new LeaveBalanceDto(
            year,
            annualEnt, annualUsed, Math.Max(0, annualEnt - annualUsed),
            emergencyUsed, carryOver,
            sickEnt,   sickUsed,   Math.Max(0, sickEnt   - sickUsed),
            unpaidUsed);
    }
}
