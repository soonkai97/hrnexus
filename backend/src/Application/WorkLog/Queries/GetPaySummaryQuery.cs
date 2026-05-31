using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Queries;

public record GetPaySummaryQuery(Guid EmployeeId, DateOnly PeriodStart, DateOnly PeriodEnd) : IRequest<PaySummaryDto>;

public record PaySummaryDto(
    Guid     EmployeeId,
    string   PeriodStart,
    string   PeriodEnd,
    decimal  TotalHours,
    decimal  GrossPay,
    List<PayShiftLineDto> Shifts);

public record PayShiftLineDto(
    Guid    ShiftId,
    string  Date,
    string  LocationName,
    decimal Hours,
    decimal Pay);

public class GetPaySummaryQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPaySummaryQuery, PaySummaryDto>
{
    public async Task<PaySummaryDto> Handle(GetPaySummaryQuery req, CancellationToken ct)
    {
        var logs = await db.WorkLogs
            .Where(w => w.EmployeeId == req.EmployeeId
                     && w.WorkDate >= req.PeriodStart
                     && w.WorkDate <= req.PeriodEnd
                     && w.EndTime != null)
            .OrderBy(w => w.WorkDate)
            .ThenBy(w => w.StartTime)
            .ToListAsync(ct);

        // Pay = total shift hours × the role's hourly rate snapshotted on the log.
        decimal totalHours = 0, totalPay = 0;
        var shifts = new List<PayShiftLineDto>();

        foreach (var w in logs)
        {
            var h     = ShiftHours.CalcHours(w.StartTime, w.EndTime!);
            var gross = h * w.HourlyRateSnapshot;
            totalHours += h;
            totalPay   += gross;

            shifts.Add(new PayShiftLineDto(
                w.Id,
                w.WorkDate.ToString("yyyy-MM-dd"),
                w.LocationName,
                Math.Round(h,     2),
                Math.Round(gross, 2)));
        }

        return new PaySummaryDto(
            req.EmployeeId,
            req.PeriodStart.ToString("yyyy-MM-dd"),
            req.PeriodEnd.ToString("yyyy-MM-dd"),
            Math.Round(totalHours, 2),
            Math.Round(totalPay, 2),
            shifts);
    }
}
