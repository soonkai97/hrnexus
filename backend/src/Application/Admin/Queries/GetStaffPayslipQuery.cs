using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

/// <summary>
/// Detailed payslip for ONE staff member over a date range: every completed shift, day by day, with
/// its hours, job, pay model, rate and computed pay — plus period totals. Drill-down behind the
/// Payroll summary; the period total here equals that staff member's Gross Pay row in the summary
/// when the range is the same month.
/// </summary>
public record GetStaffPayslipQuery(string StaffId, DateOnly From, DateOnly To) : IRequest<PayslipResult>;

public record PayslipShiftRow(
    string  WorkDate,       // "yyyy-MM-dd"
    string  StartTime,      // "HH:mm"
    string? EndDate,        // "yyyy-MM-dd" (may differ from WorkDate for overnight / multi-day)
    string? EndTime,        // "HH:mm"
    decimal Hours,          // computed shift hours (multi-day aware), rounded to 2dp
    string  Role,           // job/role name (uppercase canonical)
    string  LocationName,
    string  PayType,        // "hourly" | "fixed"
    decimal Rate,           // the rate snapshotted on the shift (per-hour, or flat-per-shift if fixed)
    decimal Pay,            // ShiftPay.Calc(payType, hours, rate), rounded to 2dp
    bool    IsEdited,
    string? Notes);

/// <summary>Per-job subtotal — groups a staff member's shifts by job + pay model, so the payslip
/// reads the way a package-based venue prices labour (e.g. "包夜 12h × 4 = 2000", "跑台 30h = 750").</summary>
public record PayslipJobSummary(
    string  Role,
    string  PayType,        // "hourly" | "fixed"
    int     ShiftCount,
    decimal Hours,
    decimal Pay);

public record PayslipResult(
    string                  StaffId,
    string                  StaffName,
    string                  From,     // "yyyy-MM-dd"
    string                  To,       // "yyyy-MM-dd"
    decimal                 TotalHours,
    decimal                 TotalPay,
    decimal                 OutstandingAdvance,  // all unsettled cash advances (running balance)
    decimal                 NetPayable,          // max(0, TotalPay − OutstandingAdvance) — what to actually pay out
    List<PayslipJobSummary> JobBreakdown,        // per-job subtotals (descending by pay)
    List<PayslipShiftRow>   Shifts);

public class GetStaffPayslipQueryHandler(IAppDbContext db)
    : IRequestHandler<GetStaffPayslipQuery, PayslipResult>
{
    public async Task<PayslipResult> Handle(GetStaffPayslipQuery req, CancellationToken ct)
    {
        if (req.To < req.From)
            throw new ArgumentException("The end date must be on or after the start date.");

        // Materialize the staff row first — FullName is a computed (non-EF-translatable) property.
        var staff = await db.Staff
            .FirstOrDefaultAsync(s => s.StaffId == req.StaffId, ct)
            ?? throw new KeyNotFoundException("Staff member not found.");

        // Completed shifts only (an open shift isn't payable yet), in the range, day-ordered.
        var logs = await db.WorkLogs
            .Where(w => w.EmployeeId == staff.Id
                     && w.EndTime != null
                     && w.WorkDate >= req.From
                     && w.WorkDate <= req.To)
            .OrderBy(w => w.WorkDate).ThenBy(w => w.StartTime)
            .ToListAsync(ct);

        var shifts = new List<PayslipShiftRow>();
        decimal totalHours = 0m, totalPay = 0m;

        foreach (var w in logs)
        {
            var endDate = w.EndDate ?? ShiftHours.DeriveEndDate(w.WorkDate, w.StartTime, w.EndTime!);
            var hours   = ShiftHours.CalcHours(w.WorkDate, w.StartTime, endDate, w.EndTime!);
            if (hours <= 0) continue;   // guard against bad data
            var pay     = ShiftPay.Calc(w.PayTypeSnapshot, hours, w.HourlyRateSnapshot);

            totalHours += hours;
            totalPay   += pay;

            shifts.Add(new PayslipShiftRow(
                WorkDate:     w.WorkDate.ToString("yyyy-MM-dd"),
                StartTime:    w.StartTime,
                EndDate:      endDate.ToString("yyyy-MM-dd"),
                EndTime:      w.EndTime,
                Hours:        Math.Round(hours, 2),
                Role:         w.RoleName,
                LocationName: w.LocationName,
                PayType:      w.PayTypeSnapshot,
                Rate:         w.HourlyRateSnapshot,
                Pay:          Math.Round(pay, 2),
                IsEdited:     w.IsEdited,
                Notes:        w.Notes));
        }

        // Per-job subtotals (group by job + pay model), heaviest-earning job first.
        var jobBreakdown = shifts
            .GroupBy(s => new { s.Role, s.PayType })
            .Select(g => new PayslipJobSummary(
                Role:       g.Key.Role,
                PayType:    g.Key.PayType,
                ShiftCount: g.Count(),
                Hours:      Math.Round(g.Sum(s => s.Hours), 2),
                Pay:        Math.Round(g.Sum(s => s.Pay), 2)))
            .OrderByDescending(j => j.Pay)
            .ToList();

        // Net payable = gross − ALL still-unsettled advances (running balance, regardless of when
        // taken), floored at 0 so the remainder simply carries as still-outstanding. This is a
        // display figure only — it does NOT settle any advance (that stays a manual admin action).
        var outstanding = await db.CashAdvances
            .Where(c => c.EmployeeId == staff.Id && c.Status == Domain.Entities.CashAdvance.Outstanding)
            .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
        var roundedTotalPay = Math.Round(totalPay, 2);
        var netPayable      = Math.Max(0m, roundedTotalPay - outstanding);

        return new PayslipResult(
            StaffId:            staff.StaffId,
            StaffName:          staff.FullName,
            From:               req.From.ToString("yyyy-MM-dd"),
            To:                 req.To.ToString("yyyy-MM-dd"),
            TotalHours:         Math.Round(totalHours, 2),
            TotalPay:           roundedTotalPay,
            OutstandingAdvance: Math.Round(outstanding, 2),
            NetPayable:         Math.Round(netPayable, 2),
            JobBreakdown:       jobBreakdown,
            Shifts:             shifts);
    }
}
