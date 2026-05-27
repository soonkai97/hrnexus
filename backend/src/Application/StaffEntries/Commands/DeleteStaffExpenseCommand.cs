using MediatR;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Commands;

public record DeleteStaffExpenseCommand(Guid ExpenseId) : IRequest;

public class DeleteStaffExpenseHandler(IAppDbContext db)
    : IRequestHandler<DeleteStaffExpenseCommand>
{
    public async Task Handle(DeleteStaffExpenseCommand req, CancellationToken ct)
    {
        var expense = await db.StaffExpenses.FindAsync([req.ExpenseId], ct)
            ?? throw new KeyNotFoundException("Staff expense not found.");

        expense.Delete();
        await db.SaveChangesAsync(ct);
    }
}
