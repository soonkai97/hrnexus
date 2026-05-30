using MediatR;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Commands;

public record DeleteStaffLoanCommand(Guid LoanId) : IRequest;

public class DeleteStaffLoanHandler(IAppDbContext db)
    : IRequestHandler<DeleteStaffLoanCommand>
{
    public async Task Handle(DeleteStaffLoanCommand req, CancellationToken ct)
    {
        var loan = await db.StaffLoans.FindAsync([req.LoanId], ct)
            ?? throw new KeyNotFoundException("Staff loan not found.");

        await PayrollLockGuard.EnsureDateOpenAsync(db, loan.LoanDate, ct);

        loan.Delete();
        await db.SaveChangesAsync(ct);
    }
}
