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

public record PayslipResult(
    string                StaffId,
    string                StaffName,
    string                From,     // "yyyy-MM-dd"
    string                To,       // "yyyy-MM-dd"
    decimal               TotalHours,
    decimal               TotalPay,
    List<PayslipShiftRow> Shifts);

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

        return new PayslipResult(
            StaffId:    staff.StaffId,
            StaffName:  staff.FullName,
            From:       req.From.ToString("yyyy-MM-dd"),
            To:         req.To.ToString("yyyy-MM-dd"),
            TotalHours: Math.Round(totalHours, 2),
            TotalPay:   Math.Round(totalPay, 2),
            Shifts:     shifts);
    }
}
