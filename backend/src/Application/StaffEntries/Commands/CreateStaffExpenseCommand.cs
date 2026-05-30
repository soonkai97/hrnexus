using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.StaffEntries.Commands;

public record CreateStaffExpenseCommand(
    Guid    StaffId,
    int     Year,
    int     Month,
    string  Category,
    decimal Amount,
    string? Notes) : IRequest<Guid>;

public class CreateStaffExpenseHandler(IAppDbContext db)
    : IRequestHandler<CreateStaffExpenseCommand, Guid>
{
    public async Task<Guid> Handle(CreateStaffExpenseCommand req, CancellationToken ct)
    {
        if (!await db.Staff.AnyAsync(s => s.Id == req.StaffId, ct))
            throw new KeyNotFoundException("Staff member not found.");

        await PayrollLockGuard.EnsureMonthOpenAsync(db, req.Year, req.Month, ct);

        var expense = StaffExpense.Create(req.StaffId, req.Year, req.Month, req.Category, req.Amount, req.Notes);
        db.StaffExpenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return expense.Id;
    }
}
