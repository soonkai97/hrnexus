using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Application.Policy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Queries;

public record GetLeaveSummaryQuery(int? Year) : IRequest<List<LeaveSummaryDto>>;

/// <summary>
/// Hours of leave per person for a year. The fixed fields are kept for existing callers;
/// <see cref="Types"/> carries one line per leave type of the company's catalogue (Phase 7q).
/// </summary>
public record LeaveSummaryDto(
    Guid    EmployeeId,
    string  StaffId,
    string  FullName,
    int     Year,
    decimal AnnualUsed,
    decimal SickUsed,
    decimal EmergencyUsed,
    decimal UnpaidUsed,
    decimal NoShowUsed,
    decimal TotalUsed,
    IReadOnlyList<LeaveTypeBalanceDto>? Types = null);

public class GetLeaveSummaryQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetLeaveSummaryQuery, List<LeaveSummaryDto>>
{
    public async Task<List<LeaveSummaryDto>> Handle(GetLeaveSummaryQuery req, CancellationToken ct)
    {
        var year        = req.Year ?? dt.LocalNow.Year;
        var startOfYear = new DateTime(year,     1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfYear   = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);

        var staff = await db.Staff
            .Where(s => s.IsActive)
            .OrderBy(s => s.FirstName).ThenBy(s => s.LastName)
            .Select(s => new { s.Id, s.StaffId, s.FirstName, s.LastName })
            .ToListAsync(ct);

        if (staff.Count == 0) return [];

        var staffIds = staff.Select(s => s.Id).ToList();

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
            // The summary carries no entitlements, so only what was used is shown (own-pool types still show their allowance).
            var b = LeaveBalanceCalculator.Compute(catalog, policy.Schedule,
                byEmployee.GetValueOrDefault(s.Id) ?? [], year, annualEntitlement: null, sickEntitlement: null);

            return new LeaveSummaryDto(
                s.Id, s.StaffId, $"{s.FirstName} {s.LastName}",
                year, b.AnnualUsed, b.SickUsed, b.EmergencyUsed, b.UnpaidUsed, b.NoShowUsed, b.TotalUsed, b.Types);
        }).ToList();
    }
}
