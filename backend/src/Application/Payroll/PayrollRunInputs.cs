using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Application.Policy;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Payroll;

/// <summary>Someone who works in the month but cannot be in the run yet, and why (listed so nobody is silently skipped).</summary>
public sealed record NotInRun(Guid EmployeeId, string StaffId, string Name, string Reason);

public sealed record PayrollRunPeople(
    MonthContext Month,
    int PolicyVersion,
    IReadOnlyList<PersonSource> People,
    IReadOnlyList<NotInRun> NotInRun);

/// <summary>
/// Reads everything a month's payroll run is calculated from: the company's settings, the rate rows, and for each
/// person who works in the month their pay history, leave, pay items and outstanding cash advances.
/// </summary>
public static class PayrollRunInputs
{
    public static async Task<PayrollRunPeople> LoadAsync(IAppDbContext db, int year, int month, CancellationToken ct)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2200)
            throw new ArgumentException("The payroll month is not a valid month.");
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var policy = await CompanyPolicyReader.GetAsync(db, ct);
        var catalog = await LeaveCatalogReader.GetAsync(db, ct);
        var rates = await StatutoryRates.LoadRulesAsync(db, ct);
        var context = new MonthContext(year, month, policy.UnpaidLeaveDivisor, policy.Schedule, catalog, AppConstants.Payroll.DeductionCapRate, rates);

        // Everyone whose employment touches the month, including people who left in it and people now inactive.
        var staff = await db.Staff.AsNoTracking().IgnoreAutoIncludes()
            .Where(s => (s.EmploymentStartDate == null || s.EmploymentStartDate <= last)
                     && (s.EmploymentEndDate == null || s.EmploymentEndDate >= first))
            .OrderBy(s => s.StaffId)
            .ToListAsync(ct);
        var ids = staff.Select(s => s.Id).ToList();

        var salaries = (await db.SalaryProfiles.AsNoTracking().Where(p => ids.Contains(p.EmployeeId)).ToListAsync(ct))
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.EffectiveFrom).Select(p => new SalaryRow(p.Id, p.EffectiveFrom, p.BasicSalary)).ToList());

        // A person is in the run when their salary covers at least one day they worked in the month.
        var inRun = new List<E.Staff>();
        var notInRun = new List<NotInRun>();
        foreach (var s in staff)
        {
            var lastWorked = s.EmploymentEndDate is { } end && end < last ? end : last;
            if (salaries.TryGetValue(s.Id, out var rows) && rows[0].EffectiveFrom <= lastWorked) inRun.Add(s);
            else notInRun.Add(new NotInRun(s.Id, s.StaffId, s.FullName, "No salary is set for the days they work this month."));
        }
        var runIds = inRun.Select(s => s.Id).ToList();

        var monthStart = DateTime.SpecifyKind(first.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var monthEnd = DateTime.SpecifyKind(last.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var leave = (await db.LeaveRequests.AsNoTracking()
                .Where(l => runIds.Contains(l.EmployeeId)
                         && (l.Status == AppConstants.Leave.Approved || l.Status == AppConstants.Leave.Pending)
                         && l.StartDate <= monthEnd && l.EndDate >= monthStart)
                .ToListAsync(ct))
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(l => new PayrollLeaveRow(l.Id, l.LeaveType, DateOnly.FromDateTime(l.StartDate),
                DateOnly.FromDateTime(l.EndDate), l.Hours, l.Status)).ToList());

        var items = (await db.PayItems.AsNoTracking()
                .Where(i => runIds.Contains(i.EmployeeId) && i.From <= last && (i.To == null || i.To >= first))
                .ToListAsync(ct))
            .GroupBy(i => i.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PayItem>)g.ToList());

        var advances = await db.CashAdvances.AsNoTracking()
            .Where(a => runIds.Contains(a.EmployeeId) && a.Status == E.CashAdvance.Outstanding && a.AdvanceDate <= last)
            .ToListAsync(ct);
        var advanceIds = advances.Select(a => a.Id).ToList();
        // Repayment amounts are encrypted, so they are summed here and not in SQL.
        var repaid = (await db.CashAdvanceRepayments.AsNoTracking().Where(r => advanceIds.Contains(r.CashAdvanceId)).ToListAsync(ct))
            .GroupBy(r => r.CashAdvanceId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var advancesByPerson = advances
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(a => new AdvanceRow(a.Id, a.Amount, a.AdvanceDate, a.RepaymentPerMonth,
                repaid.GetValueOrDefault(a.Id))).ToList());

        var people = inRun.Select(s => new PersonSource(
            s.Id, s.StaffId, s.FullName,
            s.DateOfBirth is { } dob ? DateOnly.FromDateTime(dob) : null,
            s.CitizenshipStatus, s.EmploymentStartDate, s.EmploymentEndDate,
            salaries[s.Id],
            leave.GetValueOrDefault(s.Id) ?? [],
            items.GetValueOrDefault(s.Id) ?? [],
            (IReadOnlyList<AdvanceRow>?)advancesByPerson.GetValueOrDefault(s.Id) ?? [],
            Pcb: null)).ToList();

        return new PayrollRunPeople(context, policy.Version, people, notInRun);
    }
}
