using MediatR;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Commands;

public record UpdateStaffExpenseCommand(
    Guid    ExpenseId,
    string  Category,
    decimal Amount,
    string? Notes) : IRequest;

public class UpdateStaffExpenseHandler(IAppDbContext db)
    : IRequestHandler<UpdateStaffExpenseCommand>
{
    public async Task Handle(UpdateStaffExpenseCommand req, CancellationToken ct)
    {
        var expense = await db.StaffExpenses.FindAsync([req.ExpenseId], ct)
            ?? throw new KeyNotFoundException("Staff expense not found.");

        await PayrollLockGuard.EnsureMonthOpenAsync(db, expense.Year, expense.Month, ct);

        expense.Update(req.Category, req.Amount, req.Notes);
        await db.SaveChangesAsync(ct);
    }
}
