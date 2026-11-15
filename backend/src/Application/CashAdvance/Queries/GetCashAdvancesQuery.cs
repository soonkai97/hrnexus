using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.CashAdvance.Queries;

/// <summary>
/// RepaymentPerMonth is the monthly amount payroll schedules (null = everything still owed); Repaid is what locked payroll runs
/// have already taken back; Remaining is what is still owed (the advance less what was repaid).
/// </summary>
public record CashAdvanceDto(
    Guid      Id,
    Guid      EmployeeId,
    string    StaffId,
    string    StaffName,
    decimal   Amount,
    DateOnly  AdvanceDate,
    string?   Reason,
    string    Status,
    DateTime? SettledAt,
    DateTime  CreatedAt,
    decimal?  RepaymentPerMonth,
    decimal   Repaid,
    decimal   Remaining);

/// <summary>Admin: every cash advance, newest first. Optional status filter (outstanding | settled).</summary>
public record GetCashAdvancesQuery(string? Status = null) : IRequest<List<CashAdvanceDto>>;

public class GetCashAdvancesQueryHandler(IAppDbContext db) : IRequestHandler<GetCashAdvancesQuery, List<CashAdvanceDto>>
{
    public async Task<List<CashAdvanceDto>> Handle(GetCashAdvancesQuery req, CancellationToken ct)
    {
        var q = db.CashAdvances.Include(c => c.Employee).AsQueryable();
        if (!string.IsNullOrWhiteSpace(req.Status))
            q = q.Where(c => c.Status == req.Status);

        // FullName is a computed (non-translatable) property, so materialise then project.
        var rows = await q
            .OrderByDescending(c => c.AdvanceDate).ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        // Repayment amounts are encrypted, so they are summed here and not in SQL.
        var ids = rows.Select(c => c.Id).ToList();
        var repaid = (await db.CashAdvanceRepayments.AsNoTracking().Where(r => ids.Contains(r.CashAdvanceId)).ToListAsync(ct))
            .GroupBy(r => r.CashAdvanceId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        return rows.Select(c =>
        {
            var taken = repaid.GetValueOrDefault(c.Id);
            return new CashAdvanceDto(
                c.Id, c.EmployeeId, c.Employee.StaffId, c.Employee.FullName,
                c.Amount, c.AdvanceDate, c.Reason, c.Status, c.SettledAt, c.CreatedAt,
                c.RepaymentPerMonth, taken, c.Status == Domain.Entities.CashAdvance.Settled ? 0m : Math.Max(0m, c.Amount - taken));
        }).ToList();
    }
}
