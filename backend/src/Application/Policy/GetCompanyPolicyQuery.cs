using HRNexus.Application.Common.Interfaces;
using MediatR;

namespace HRNexus.Application.Policy;

/// <param name="WorkingDays">ISO weekday numbers, 1 = Monday ... 7 = Sunday.</param>
/// <param name="WorkdayHours">Hours in one working day.</param>
/// <param name="AnnualLeaveAdvanceDays">Days of notice annual leave needs.</param>
/// <param name="MaxLeaveDays">Longest single leave request, in calendar days.</param>
/// <param name="CarryOverMode">none, capped or unlimited.</param>
/// <param name="CarryOverMaxHours">The cap used when the mode is capped.</param>
/// <param name="UnpaidLeaveDivisor">Divisor for unpaid-leave deductions in payroll.</param>
/// <param name="Version">Send it back when saving; a stale version is refused.</param>
public record CompanyPolicyDto(int[] WorkingDays, decimal WorkdayHours, int AnnualLeaveAdvanceDays, int MaxLeaveDays,
    string CarryOverMode, decimal CarryOverMaxHours, decimal UnpaidLeaveDivisor, int Version);

/// <summary>The company policy. Readable by every signed-in user: both apps need the working week to preview leave hours.</summary>
public record GetCompanyPolicyQuery : IRequest<CompanyPolicyDto>;

public class GetCompanyPolicyQueryHandler(IAppDbContext db) : IRequestHandler<GetCompanyPolicyQuery, CompanyPolicyDto>
{
    public async Task<CompanyPolicyDto> Handle(GetCompanyPolicyQuery req, CancellationToken ct)
    {
        var p = await CompanyPolicyReader.GetAsync(db, ct);
        return new CompanyPolicyDto(p.Schedule.IsoDays, p.WorkdayHours, p.AnnualLeaveAdvanceDays, p.MaxLeaveDays,
            p.CarryOverMode, p.CarryOverMaxHours, p.UnpaidLeaveDivisor, p.Version);
    }
}
