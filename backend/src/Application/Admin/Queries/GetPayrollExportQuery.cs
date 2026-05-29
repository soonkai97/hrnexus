using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.Admin.Queries;

/// <summary>Returns CSV-ready payroll rows for all active employees in a given month.</summary>
public record GetPayrollExportQuery(int Year, int Month) : IRequest<PayrollExportResult>;

public record PayrollExportRow(
    string  StaffId,
    string  FullName,
    string  Roles,
    decimal HourlyRate,
    decimal TotalHours,
    decimal GrossPay,           // work-hours pay only (total hours × hourly rate)
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
            .Where(e => e.IsActive
                     && !db.StaffRoles.Any(r => r.EmployeeId == e.Id && r.Role == AppConstants.Roles.Admin))
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
            var empLogs = logs.Where(l => l.EmployeeId == emp.Id).ToList();

            // Pay = total shift hours × the role's hourly rate snapshotted on each log.
            var totalHours = 0m; var grossWork = 0m;
            foreach (var w in empLogs)
            {
                var h = ShiftHours.CalcHours(w.StartTime, w.EndTime!);
                if (h <= 0) continue;   // guard against bad data
                totalHours += h;
                grossWork  += h * w.HourlyRateSnapshot;
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
            var grossWorkPay  = Math.Round(grossWork, 2);

            rows.Add(new PayrollExportRow(
                StaffId:          emp.StaffId,
                FullName:         emp.FullName,
                Roles:            string.Join(" / ", emp.Roles),
                HourlyRate:       displayRate,
                TotalHours:       Math.Round(totalHours, 2),
                GrossPay:         grossWorkPay,
                ApprovedLeaveDays: totalLeaveDays,
                PaidLeaveDays:    paidLeaveDays,
                PaidLeavePay:     paidLeavePay,
                GrandTotal:       grossWorkPay + paidLeavePay));
        }

        return new PayrollExportResult(startDt.ToString("yyyy-MM"), rows);
    }
}
