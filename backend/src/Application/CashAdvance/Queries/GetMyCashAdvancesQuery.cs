using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.CashAdvance.Queries;

/// <summary>Staff: the caller's own cash advances, newest first (read-only history).</summary>
public record GetMyCashAdvancesQuery(Guid EmployeeId) : IRequest<List<CashAdvanceDto>>;

public class GetMyCashAdvancesQueryHandler(IAppDbContext db) : IRequestHandler<GetMyCashAdvancesQuery, List<CashAdvanceDto>>
{
    public async Task<List<CashAdvanceDto>> Handle(GetMyCashAdvancesQuery req, CancellationToken ct)
    {
        var rows = await db.CashAdvances
            .Include(c => c.Employee)
            .Where(c => c.EmployeeId == req.EmployeeId)
            .OrderByDescending(c => c.AdvanceDate).ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(c => new CashAdvanceDto(
            c.Id, c.EmployeeId, c.Employee.StaffId, c.Employee.FullName,
            c.Amount, c.AdvanceDate, c.Reason, c.Status, c.SettledAt, c.CreatedAt)).ToList();
    }
}
