using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.StaffEntries.Commands;

public record CreateStaffLoanCommand(
    Guid    StaffId,
    string  LoanDate,
    string  LoanType,
    decimal Amount,
    string? Purpose) : IRequest<Guid>;

public class CreateStaffLoanHandler(IAppDbContext db)
    : IRequestHandler<CreateStaffLoanCommand, Guid>
{
    public async Task<Guid> Handle(CreateStaffLoanCommand req, CancellationToken ct)
    {
        if (!await db.Staff.AnyAsync(s => s.Id == req.StaffId, ct))
            throw new KeyNotFoundException("Staff member not found.");

        if (!DateOnly.TryParse(req.LoanDate, out var date))
            throw new ArgumentException("Invalid date format. Use yyyy-MM-dd.");

        await PayrollLockGuard.EnsureDateOpenAsync(db, date, ct);

        var loan = StaffLoan.Create(req.StaffId, date, req.LoanType, req.Amount, req.Purpose);
        db.StaffLoans.Add(loan);
        await db.SaveChangesAsync(ct);
        return loan.Id;
    }
}
