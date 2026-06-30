using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.CashAdvance.Commands;

/// <summary>Admin records a salary advance given to a staff member (records only — moves no money).</summary>
public record CreateCashAdvanceCommand(Guid EmployeeId, decimal Amount, DateOnly AdvanceDate, string? Reason, string RecordedBy)
    : IRequest<Guid>;

public class CreateCashAdvanceCommandHandler(IAppDbContext db) : IRequestHandler<CreateCashAdvanceCommand, Guid>
{
    public async Task<Guid> Handle(CreateCashAdvanceCommand req, CancellationToken ct)
    {
        if (req.Amount <= 0) throw new ArgumentException("Cash advance amount must be greater than zero.");

        var exists = await db.Staff.AnyAsync(s => s.Id == req.EmployeeId, ct);
        if (!exists) throw new KeyNotFoundException("Staff member not found.");

        var advance = Domain.Entities.CashAdvance.Create(req.EmployeeId, req.Amount, req.AdvanceDate, req.Reason, req.RecordedBy);
        db.CashAdvances.Add(advance);
        await db.SaveChangesAsync(ct);
        return advance.Id;
    }
}
