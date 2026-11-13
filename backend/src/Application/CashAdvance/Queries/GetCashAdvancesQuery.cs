using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.CashAdvance.Queries;

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
    DateTime  CreatedAt);

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

        return rows.Select(c => new CashAdvanceDto(
            c.Id, c.EmployeeId, c.Employee.StaffId, c.Employee.FullName,
            c.Amount, c.AdvanceDate, c.Reason, c.Status, c.SettledAt, c.CreatedAt)).ToList();
    }
}
