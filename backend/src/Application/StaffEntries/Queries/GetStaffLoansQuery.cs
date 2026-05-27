using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Queries;

public record GetStaffLoansQuery(Guid StaffId) : IRequest<List<StaffLoanDto>>;

public record StaffLoanDto(
    string   Id,
    string   StaffId,
    string   LoanDate,
    string   LoanType,
    decimal  Amount,
    string?  Purpose,
    DateTime CreatedAt);

public class GetStaffLoansHandler(IAppDbContext db)
    : IRequestHandler<GetStaffLoansQuery, List<StaffLoanDto>>
{
    public async Task<List<StaffLoanDto>> Handle(GetStaffLoansQuery req, CancellationToken ct) =>
        await db.StaffLoans
            .Where(l => l.StaffId == req.StaffId)
            .OrderBy(l => l.LoanDate)
            .Select(l => new StaffLoanDto(
                l.Id.ToString(), l.StaffId.ToString(),
                l.LoanDate.ToString("yyyy-MM-dd"), l.LoanType,
                l.Amount, l.Purpose, l.CreatedAt))
            .ToListAsync(ct);
}
