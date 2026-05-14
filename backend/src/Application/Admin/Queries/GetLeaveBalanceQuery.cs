using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Queries;

public record GetLeaveBalanceQuery(Guid EmployeeId, int? Year) : IRequest<LeaveBalanceDto>;

public record LeaveBalanceDto(
    int    Year,
    int    AnnualEntitlement,
    double AnnualUsed,
    double AnnualRemaining,
    int    SickEntitlement,
    double SickUsed,
    double SickRemaining,
    double UnpaidUsed);

public class GetLeaveBalanceQueryHandler(IAppDbContext db)
    : IRequestHandler<GetLeaveBalanceQuery, LeaveBalanceDto>
{
    public async Task<LeaveBalanceDto> Handle(GetLeaveBalanceQuery req, CancellationToken ct)
    {
        var year = req.Year ?? DateTime.UtcNow.Year;

        // Get or return defaults
        var ent = await db.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.Year == year, ct);

        var annualEnt = ent?.AnnualDays ?? 14;
        var sickEnt   = ent?.SickDays   ?? 14;

        // Fetch approved leave records for this year
        var startOfYear = new DateTime(year,     1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfYear   = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);

        var approvedLeaves = await db.LeaveRequests
            .Where(l => l.EmployeeId == req.EmployeeId
                     && l.Status == "approved"
                     && l.StartDate >= startOfYear
                     && l.StartDate <= endOfYear)
            .Select(l => new { l.LeaveType, l.StartDate, l.EndDate })
            .ToListAsync(ct);

        static double CalcDays(DateTime start, DateTime end) =>
            (end.Date - start.Date).TotalDays + 1;

        var annualUsed = approvedLeaves.Where(l => l.LeaveType == "annual").Sum(l => CalcDays(l.StartDate, l.EndDate));
        var sickUsed   = approvedLeaves.Where(l => l.LeaveType == "sick"  ).Sum(l => CalcDays(l.StartDate, l.EndDate));
        var unpaidUsed = approvedLeaves.Where(l => l.LeaveType == "unpaid" ).Sum(l => CalcDays(l.StartDate, l.EndDate));

        return new LeaveBalanceDto(
            year,
            annualEnt, annualUsed, Math.Max(0, annualEnt - annualUsed),
            sickEnt,   sickUsed,   Math.Max(0, sickEnt   - sickUsed),
            unpaidUsed);
    }
}
