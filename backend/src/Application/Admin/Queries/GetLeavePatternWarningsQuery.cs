using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

/// <summary>
/// Returns active staff who have taken sick leave 3 or more times in the rolling 90-day window.
/// Used to surface frequent-absence patterns so managers can follow up early.
/// </summary>
public record GetLeavePatternWarningsQuery : IRequest<List<LeavePatternWarningDto>>;

public record LeavePatternWarningDto(
    Guid   EmployeeId,
    string StaffId,
    string FullName,
    int    SickLeaveCount,   // distinct sick leave requests in the last 90 days
    decimal SickLeaveHours); // total sick hours in the last 90 days

public class GetLeavePatternWarningsQueryHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<GetLeavePatternWarningsQuery, List<LeavePatternWarningDto>>
{
    private const int WindowDays  = 90;
    private const int MinCount    = 3;   // flag when >= 3 sick requests in window

    public async Task<List<LeavePatternWarningDto>> Handle(
        GetLeavePatternWarningsQuery req, CancellationToken ct)
    {
        var windowStart = dt.LocalNow.Date.AddDays(-WindowDays);
        var windowStartUtc = DateTime.SpecifyKind(windowStart, DateTimeKind.Utc);

        // Sick leave requests approved or pending in the rolling window
        var sickLeaves = await db.LeaveRequests
            .Include(l => l.Employee)
            .Where(l => l.LeaveType == "sick"
                     && l.StartDate >= windowStartUtc
                     && l.Status != "rejected"
                     && l.Status != "cancelled"
                     && l.Employee.IsActive)
            .Select(l => new { l.EmployeeId, l.Employee.StaffId,
                FullName = l.Employee.FirstName + " " + l.Employee.LastName,
                l.Hours })
            .ToListAsync(ct);

        return sickLeaves
            .GroupBy(l => l.EmployeeId)
            .Where(g => g.Count() >= MinCount)
            .Select(g => new LeavePatternWarningDto(
                g.Key,
                g.First().StaffId,
                g.First().FullName,
                g.Count(),
                g.Sum(l => l.Hours ?? 0m)))
            .OrderByDescending(w => w.SickLeaveCount)
            .ToList();
    }
}
