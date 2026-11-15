using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.CashAdvance.Commands;

/// <summary>Toggle whether an advance has been settled (deducted from salary).</summary>
public record SetCashAdvanceSettledCommand(Guid Id, bool Settled) : IRequest;

public class SetCashAdvanceSettledCommandHandler(IAppDbContext db) : IRequestHandler<SetCashAdvanceSettledCommand>
{
    public async Task Handle(SetCashAdvanceSettledCommand req, CancellationToken ct) =>
        await SerializableWork.RunAsync(db, async c =>
        {
            var advance = await db.CashAdvances.FirstOrDefaultAsync(a => a.Id == req.Id, c)
                ?? throw new KeyNotFoundException("Cash advance not found.");
            await CashAdvanceGuard.EnsureNotInPayrollAsync(db, req.Id, c);

            if (req.Settled) advance.MarkSettled();
            else             advance.Reopen();

            await db.SaveChangesAsync(c);
            return 0;
        }, ct);
}
