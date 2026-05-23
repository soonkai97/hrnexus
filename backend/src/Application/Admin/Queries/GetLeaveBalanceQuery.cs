using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Queries;

public record GetLeaveBalanceQuery(Guid EmployeeId, int? Year) : IRequest<LeaveBalanceDto>;

public record LeaveBalanceDto(
    int     Year,
    decimal AnnualEntitlement,
    decimal AnnualUsed,        // annual + emergency combined (both draw from the annual pool)
    decimal AnnualRemaining,
    decimal EmergencyUsed,     // emergency portion within AnnualUsed, shown separately for clarity
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

        var annualEnt = ent?.AnnualHours ?? 0m;
        var sickEnt   = ent?.SickHours   ?? 0m;

        var startOfYear = new DateTime(year,     1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfYear   = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);

        var countedLeaves = await db.LeaveRequests
            .Where(l => l.EmployeeId == req.EmployeeId
                     && l.Status != "rejected"
                     && l.Status != "cancelled"
                     && l.StartDate >= startOfYear
                     && l.StartDate <= endOfYear)
            .Select(l => new { l.LeaveType, l.Hours })
            .ToListAsync(ct);

        // Annual + emergency both count against the annual pool
        var annualUsed    = countedLeaves.Where(l => l.LeaveType == "annual" || l.LeaveType == "emergency").Sum(l => l.Hours ?? 0m);
        var emergencyUsed = countedLeaves.Where(l => l.LeaveType == "emergency").Sum(l => l.Hours ?? 0m);
        var sickUsed      = countedLeaves.Where(l => l.LeaveType == "sick"  ).Sum(l => l.Hours ?? 0m);
        var unpaidUsed    = countedLeaves.Where(l => l.LeaveType == "unpaid" ).Sum(l => l.Hours ?? 0m);

        return new LeaveBalanceDto(
            year,
            annualEnt, annualUsed, Math.Max(0, annualEnt - annualUsed),
            emergencyUsed,
            sickEnt,   sickUsed,   Math.Max(0, sickEnt   - sickUsed),
            unpaidUsed);
    }
}
