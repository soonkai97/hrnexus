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
    decimal  RegularHours,
    decimal  OvertimeHours,
    decimal  RegularPay,
    decimal  OvertimePay,
    decimal  TipsAllocated,
    decimal  GrossPay,
    decimal  OtMultiplier,
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

        var allOtRules = await db.OvertimeRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.ThresholdHours)
            .ToListAsync(ct);

        var emp = await db.Staff.FindAsync([req.EmployeeId], ct);
        var applicableRules = OvertimeCalculator.ResolveRules(allOtRules, emp?.Roles ?? []);
        var otThreshold     = OvertimeCalculator.GetThreshold(applicableRules);

        decimal totalRegHours = 0, totalOtHours = 0, totalRegPay = 0, totalOtPay = 0;
        var shifts = new List<PayShiftLineDto>();

        foreach (var w in logs)
        {
            var h = OvertimeCalculator.CalcHours(w.StartTime, w.EndTime!);

            var gross  = OvertimeCalculator.Calculate(h, w.HourlyRateSnapshot, applicableRules);
            var reg    = Math.Min(h, otThreshold);
            var ot     = Math.Max(0m, h - otThreshold);
            var regPay = reg * w.HourlyRateSnapshot;
            var otPay  = gross - regPay;

            totalRegHours += reg;
            totalOtHours  += ot;
            totalRegPay   += regPay;
            totalOtPay    += otPay;

            shifts.Add(new PayShiftLineDto(
                w.Id,
                w.WorkDate.ToString("yyyy-MM-dd"),
                w.LocationName,
                Math.Round(h,     2),
                Math.Round(gross, 2)));
        }

        var otMultiplier = totalOtHours > 0
            ? (applicableRules.Count > 0
                ? applicableRules.OrderByDescending(r => r.Multiplier).First().Multiplier
                : 1.5m)
            : 1.0m;

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
            Math.Round(totalRegHours + totalOtHours, 2),
            Math.Round(totalRegHours, 2),
            Math.Round(totalOtHours,  2),
            Math.Round(totalRegPay,   2),
            Math.Round(totalOtPay,    2),
            Math.Round(tipsAllocated, 2),
            Math.Round(totalRegPay + totalOtPay + tipsAllocated, 2),
            otMultiplier,
            shifts);
    }
}
