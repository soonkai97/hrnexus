using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

/// <summary>Returns CSV-ready payroll rows for all active employees in a given month.</summary>
public record GetPayrollExportQuery(int Year, int Month) : IRequest<PayrollExportResult>;

public record PayrollExportRow(
    string  StaffId,
    string  FullName,
    string  Roles,
    decimal HourlyRate,
    decimal TotalHours,
    decimal GrossPay,            // work-hours pay = total hours × hourly rate. ALL leave is unpaid, so this is the full payout.
    int     ApprovedLeaveDays);  // total approved leave days taken in the period (informational; unpaid)

public record PayrollExportResult(
    string                 Month,
    List<PayrollExportRow> Rows);

public class GetPayrollExportQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPayrollExportQuery, PayrollExportResult>
{
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
                if (w.EndTime == null) continue;   // active shift — not yet payable
                var endDate = w.EndDate ?? ShiftHours.DeriveEndDate(w.WorkDate, w.StartTime, w.EndTime);
                var h = ShiftHours.CalcHours(w.WorkDate, w.StartTime, endDate, w.EndTime);
                if (h <= 0) continue;   // guard against bad data
                // Billed hours (hourly floors to 0.5 blocks) so the export's Total Hours matches the pay.
                totalHours += ShiftHours.BillableHours(w.PayTypeSnapshot, h);
                grossWork  += ShiftPay.Calc(w.PayTypeSnapshot, h, w.HourlyRateSnapshot);
            }

            // Hourly rate: prefer current-period snapshot, fall back to latest historical
            var displayRate = empLogs.OrderByDescending(w => w.CreatedAt)
                                     .FirstOrDefault()?.HourlyRateSnapshot
                           ?? latestRates.GetValueOrDefault(emp.Id, 0m);

            // Leave taken in the period (informational only — all leave is UNPAID, so it adds no pay).
            var empLeaves = leaves.Where(l => l.EmployeeId == emp.Id).ToList();
            var totalLeaveDays = empLeaves.Sum(l =>
                (int)Math.Round((l.EndDate.Date - l.StartDate.Date).TotalDays) + 1);

            rows.Add(new PayrollExportRow(
                StaffId:          emp.StaffId,
                FullName:         emp.FullName,
                Roles:            string.Join(" / ", emp.Roles),
                HourlyRate:       displayRate,
                TotalHours:       Math.Round(totalHours, 2),
                GrossPay:         Math.Round(grossWork, 2),
                ApprovedLeaveDays: totalLeaveDays));
        }

        return new PayrollExportResult(startDt.ToString("yyyy-MM"), rows);
    }
}
