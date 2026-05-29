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
    decimal  TipsAllocated,
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

        // Sum tips allocations for the employee within the period.
        // TipsPool.ShiftDate is stored as UTC midnight; convert DateOnly period bounds accordingly.
        var startDt = DateTime.SpecifyKind(req.PeriodStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endDt   = DateTime.SpecifyKind(req.PeriodEnd.ToDateTime(TimeOnly.MinValue),   DateTimeKind.Utc).AddDays(1);

        var tipsAllocated = await db.TipsAllocations
            .Where(a => a.EmployeeId == req.EmployeeId)
            .Join(db.TipsPools,
                a => a.PoolId,
                p => p.Id,
                (a, p) => new { a.Amount, p.ShiftDate })
            .Where(x => x.ShiftDate >= startDt && x.ShiftDate < endDt)
            .SumAsync(x => x.Amount, ct);

        return new PaySummaryDto(
            req.EmployeeId,
            req.PeriodStart.ToString("yyyy-MM-dd"),
            req.PeriodEnd.ToString("yyyy-MM-dd"),
            Math.Round(totalHours, 2),
            Math.Round(tipsAllocated, 2),
            Math.Round(totalPay + tipsAllocated, 2),
            shifts);
    }
}
