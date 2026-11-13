using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Application.Policy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Queries;

public record GetLeaveSummaryQuery(int? Year) : IRequest<List<LeaveSummaryDto>>;

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
    decimal TotalUsed);

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
            var emp          = byEmployee.GetValueOrDefault(s.Id);
            var annualUsed   = emp?.Where(l => l.LeaveType == "annual"   ).Sum(l => l.Hours ?? 0m) ?? 0m;
            var sickUsed     = emp?.Where(l => l.LeaveType == "sick"     ).Sum(l => l.Hours ?? 0m) ?? 0m;
            var emergencyUsed= emp?.Where(l => l.LeaveType == "emergency").Sum(l => l.Hours ?? 0m) ?? 0m;
            var unpaidUsed   = emp?.Where(l => l.LeaveType == "unpaid"   ).Sum(l => l.Hours ?? 0m) ?? 0m;
            var noShowUsed   = emp?.Where(l => l.LeaveType == "no-show"  ).Sum(l => l.Hours ?? 0m) ?? 0m;
            var total        = annualUsed + sickUsed + emergencyUsed + unpaidUsed + noShowUsed;

            return new LeaveSummaryDto(
                s.Id, s.StaffId, $"{s.FirstName} {s.LastName}",
                year, annualUsed, sickUsed, emergencyUsed, unpaidUsed, noShowUsed, total);
        }).ToList();
    }
}
