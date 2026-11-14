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
    decimal UnpaidUsed,
    IReadOnlyList<LeaveTypeBalanceDto>? Types = null);

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
        var catalog = await LeaveCatalogReader.GetAsync(db, ct);
        var leaves = await db.LeaveRequests
            .Where(l => staffIds.Contains(l.EmployeeId)
                     && l.Status != "rejected"
                     && l.Status != "cancelled"
                     && l.StartDate <= endOfYear
                     && l.EndDate   >= startOfYear)
            .Select(l => new { l.EmployeeId, Row = new LeaveRow(l.LeaveType, l.StartDate, l.EndDate, l.Hours) })
            .ToListAsync(ct);

        var byEmployee = leaves
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Row).ToList());

        return staff.Select(s =>
        {
            var ent       = entitlements.GetValueOrDefault(s.Id);
            var annualEnt = ent?.TotalAnnualHours ?? 0m;   // base + carry-over
            var sickEnt   = ent?.SickHours     ?? 0m;
            var carryOver = ent?.CarryOverHours ?? 0m;

            // A leave across New Year is charged to each year by the days that fall in it (counted by the type's schedule).
            var b = LeaveBalanceCalculator.Compute(catalog, policy.Schedule,
                byEmployee.GetValueOrDefault(s.Id) ?? [], year, annualEnt, sickEnt);

            return new StaffLeaveBalanceDto(
                s.Id,
                s.StaffId,
                $"{s.FirstName} {s.LastName}",
                year,
                annualEnt, b.AnnualPoolUsed, Math.Max(0, annualEnt - b.AnnualPoolUsed),
                b.EmergencyUsed, carryOver,
                sickEnt,   b.SickPoolUsed,   Math.Max(0, sickEnt - b.SickPoolUsed),
                b.UnpaidUsed,
                b.Types);
        }).ToList();
    }
}
