using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.CashAdvance.Commands;

/// <summary>Remove a cash-advance record (e.g. entered by mistake). Admin-only.</summary>
public record DeleteCashAdvanceCommand(Guid Id) : IRequest;

public class DeleteCashAdvanceCommandHandler(IAppDbContext db) : IRequestHandler<DeleteCashAdvanceCommand>
{
    public async Task Handle(DeleteCashAdvanceCommand req, CancellationToken ct)
    {
        var advance = await db.CashAdvances.FirstOrDefaultAsync(c => c.Id == req.Id, ct)
            ?? throw new KeyNotFoundException("Cash advance not found.");

        db.CashAdvances.Remove(advance);
        await db.SaveChangesAsync(ct);
    }
}
