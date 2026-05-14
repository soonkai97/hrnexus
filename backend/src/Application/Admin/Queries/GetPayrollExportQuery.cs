using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Admin.Queries;

/// <summary>Returns CSV-ready payroll rows for all active employees in a given month.</summary>
public record GetPayrollExportQuery(int Year, int Month) : IRequest<PayrollExportResult>;

public record PayrollExportRow(
    string  StaffId,
    string  FullName,
    string  Roles,
    decimal HourlyRate,
    decimal TotalHours,
    decimal RegularHours,
    decimal OvertimeHours,
    decimal RegularPay,
    decimal OvertimePay,
    decimal GrossPay,           // work-hours pay only
    int     ApprovedLeaveDays,  // total approved leave days (all types)
    int     PaidLeaveDays,      // annual + sick only
    decimal PaidLeavePay,       // PaidLeaveDays × 8h × HourlyRate
    decimal GrandTotal);        // GrossPay + PaidLeavePay

public record PayrollExportResult(
    string                 Month,
    List<PayrollExportRow> Rows);

public class GetPayrollExportQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPayrollExportQuery, PayrollExportResult>
{
    private const decimal PaidLeaveHoursPerDay = 8m;

    public async Task<PayrollExportResult> Handle(GetPayrollExportQuery req, CancellationToken ct)
    {
        var start   = new DateOnly(req.Year, req.Month, 1);
        var end     = start.AddMonths(1).AddDays(-1);
        var startDt = new DateTime(req.Year, req.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDt   = startDt.AddMonths(1).AddTicks(-1);

        var employees = await db.Staff
            .Where(e => e.IsActive)
            .OrderBy(e => e.StaffId)
            .ToListAsync(ct);

        // Completed work logs in period
        var logs = await db.WorkLogs
            .Where(w => w.WorkDate >= start && w.WorkDate <= end && w.EndTime != null)
            .ToListAsync(ct);

        // Approved leaves in period
        var leaves = await db.LeaveRequests
            .Where(l => l.Status == "approved"
                     && l.StartDate <= endDt
                     && l.EndDate   >= startDt)
            .ToListAsync(ct);

        // Load all active OT rules once
        var allOtRules = await db.OvertimeRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.ThresholdHours)
            .ToListAsync(ct);

        // Latest hourly rate per employee (for paid-leave pay when no current-period logs)
        var latestRates = await db.WorkLogs
            .Where(w => w.EndTime != null)
            .GroupBy(w => w.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Rate = g.OrderByDescending(w => w.CreatedAt).First().HourlyRateSnapshot,
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Rate, ct);

        var rows = new List<PayrollExportRow>();

        foreach (var emp in employees)
        {
            var empLogs         = logs.Where(l => l.EmployeeId == emp.Id).ToList();
            var applicableRules = OvertimeCalculator.ResolveRules(allOtRules, emp.Roles);
            var otThreshold     = OvertimeCalculator.GetThreshold(applicableRules);

            var regHours = 0m; var otHours = 0m;
            var regPay   = 0m; var otPay   = 0m;

            foreach (var w in empLogs)
            {
                var h = OvertimeCalculator.CalcHours(w.StartTime, w.EndTime!);
                // Clamp to 0 in case of bad data (should not happen after time validation, but be safe)
                if (h <= 0) continue;

                var gross = OvertimeCalculator.Calculate(h, w.HourlyRateSnapshot, applicableRules);

                var reg = Math.Min(h, otThreshold);
                var ot  = Math.Max(0m, h - otThreshold);
                regHours += reg;
                otHours  += ot;
                regPay   += reg * w.HourlyRateSnapshot;
                otPay    += gross - (reg * w.HourlyRateSnapshot);
            }

            // Hourly rate: prefer current-period snapshot, fall back to latest historical
            var displayRate = empLogs.OrderByDescending(w => w.CreatedAt)
                                     .FirstOrDefault()?.HourlyRateSnapshot
                           ?? latestRates.GetValueOrDefault(emp.Id, 0m);

            // Leave breakdown for this employee
            var empLeaves = leaves.Where(l => l.EmployeeId == emp.Id).ToList();

            var totalLeaveDays = empLeaves.Sum(l =>
                (int)Math.Round((l.EndDate.Date - l.StartDate.Date).TotalDays) + 1);

            // Only annual + sick are compensated; unpaid / no-show are not
            var paidLeaves = empLeaves.Where(l =>
                l.LeaveType == "annual" || l.LeaveType == "sick").ToList();

            var paidLeaveDays = paidLeaves.Sum(l =>
                (int)Math.Round((l.EndDate.Date - l.StartDate.Date).TotalDays) + 1);

            var paidLeavePay  = Math.Round(paidLeaveDays * PaidLeaveHoursPerDay * displayRate, 2);
            var grossWorkPay  = Math.Round(regPay + otPay, 2);

            rows.Add(new PayrollExportRow(
                StaffId:          emp.StaffId,
                FullName:         emp.FullName,
                Roles:            string.Join(" / ", emp.Roles),
                HourlyRate:       displayRate,
                TotalHours:       Math.Round(regHours + otHours, 2),
                RegularHours:     Math.Round(regHours, 2),
                OvertimeHours:    Math.Round(otHours, 2),
                RegularPay:       Math.Round(regPay, 2),
                OvertimePay:      Math.Round(otPay, 2),
                GrossPay:         grossWorkPay,
                ApprovedLeaveDays: totalLeaveDays,
                PaidLeaveDays:    paidLeaveDays,
                PaidLeavePay:     paidLeavePay,
                GrandTotal:       grossWorkPay + paidLeavePay));
        }

        return new PayrollExportResult(startDt.ToString("yyyy-MM"), rows);
    }
}
