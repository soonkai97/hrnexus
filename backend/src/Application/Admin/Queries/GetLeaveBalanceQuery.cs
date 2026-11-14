using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Application.Policy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Queries;

public record GetLeaveBalanceQuery(Guid EmployeeId, int? Year) : IRequest<LeaveBalanceDto>;

/// <summary>
/// One person's balances for a year. The fixed annual/sick/emergency/unpaid fields are kept for existing
/// callers; <see cref="Types"/> carries one line per leave type of the company's catalogue (Phase 7q).
/// </summary>
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
    decimal UnpaidUsed,
    IReadOnlyList<LeaveTypeBalanceDto>? Types = null);

public class GetLeaveBalanceQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetLeaveBalanceQuery, LeaveBalanceDto>
{
    public async Task<LeaveBalanceDto> Handle(GetLeaveBalanceQuery req, CancellationToken ct)
    {
        var year = req.Year ?? dt.LocalNow.Year;

        var ent = await db.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.Year == year, ct);

        var annualEnt   = ent?.TotalAnnualHours ?? 0m;   // base + carry-over
        var sickEnt     = ent?.SickHours     ?? 0m;
        var carryOver   = ent?.CarryOverHours ?? 0m;

        var startOfYear = new DateTime(year, 1, 1,   0, 0, 0, DateTimeKind.Utc);
        var endOfYear   = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var policy = await CompanyPolicyReader.GetAsync(db, ct);
        var catalog = await LeaveCatalogReader.GetAsync(db, ct);
        var countedLeaves = await db.LeaveRequests
            .Where(l => l.EmployeeId == req.EmployeeId
                     && l.Status != "rejected"
                     && l.Status != "cancelled"
                     && l.StartDate <= endOfYear
                     && l.EndDate   >= startOfYear)
            .Select(l => new LeaveRow(l.LeaveType, l.StartDate, l.EndDate, l.Hours))
            .ToListAsync(ct);
        // A leave across New Year is charged to each year by the days that fall in it (counted by the type's schedule).
        var b = LeaveBalanceCalculator.Compute(catalog, policy.Schedule, countedLeaves, year, annualEnt, sickEnt);

        return new LeaveBalanceDto(
            year,
            annualEnt, b.AnnualPoolUsed, Math.Max(0, annualEnt - b.AnnualPoolUsed),
            b.EmergencyUsed, carryOver,
            sickEnt,   b.SickPoolUsed,   Math.Max(0, sickEnt - b.SickPoolUsed),
            b.UnpaidUsed,
            b.Types);
    }
}
