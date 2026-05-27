using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Queries;

public record GetStaffExpensesQuery(Guid StaffId, int Year, int Month) : IRequest<List<StaffExpenseDto>>;

public record StaffExpenseDto(
    string  Id,
    string  StaffId,
    int     Year,
    int     Month,
    string  Category,
    decimal Amount,
    string? Notes);

public class GetStaffExpensesHandler(IAppDbContext db)
    : IRequestHandler<GetStaffExpensesQuery, List<StaffExpenseDto>>
{
    public async Task<List<StaffExpenseDto>> Handle(GetStaffExpensesQuery req, CancellationToken ct) =>
        await db.StaffExpenses
            .Where(e => e.StaffId == req.StaffId && e.Year == req.Year && e.Month == req.Month)
            .OrderBy(e => e.Category)
            .Select(e => new StaffExpenseDto(
                e.Id.ToString(), e.StaffId.ToString(), e.Year, e.Month, e.Category, e.Amount, e.Notes))
            .ToListAsync(ct);
}
