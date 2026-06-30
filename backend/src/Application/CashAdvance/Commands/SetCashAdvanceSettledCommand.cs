using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.CashAdvance.Commands;

/// <summary>Toggle whether an advance has been settled (deducted from salary).</summary>
public record SetCashAdvanceSettledCommand(Guid Id, bool Settled) : IRequest;

public class SetCashAdvanceSettledCommandHandler(IAppDbContext db) : IRequestHandler<SetCashAdvanceSettledCommand>
{
    public async Task Handle(SetCashAdvanceSettledCommand req, CancellationToken ct)
    {
        var advance = await db.CashAdvances.FirstOrDefaultAsync(c => c.Id == req.Id, ct)
            ?? throw new KeyNotFoundException("Cash advance not found.");

        if (req.Settled) advance.MarkSettled();
        else             advance.Reopen();

        await db.SaveChangesAsync(ct);
    }
}
