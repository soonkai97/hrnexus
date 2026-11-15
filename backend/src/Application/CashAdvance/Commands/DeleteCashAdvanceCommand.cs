using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.CashAdvance.Commands;

/// <summary>Remove a cash-advance record (e.g. entered by mistake). Admin-only.</summary>
public record DeleteCashAdvanceCommand(Guid Id) : IRequest;

public class DeleteCashAdvanceCommandHandler(IAppDbContext db) : IRequestHandler<DeleteCashAdvanceCommand>
{
    public async Task Handle(DeleteCashAdvanceCommand req, CancellationToken ct) =>
        // The payroll check and the delete are one Serializable transaction: a run submitted between them would otherwise
        // freeze a deduction for an advance that is then deleted.
        await SerializableWork.RunAsync(db, async c =>
        {
            var advance = await db.CashAdvances.FirstOrDefaultAsync(a => a.Id == req.Id, c)
                ?? throw new KeyNotFoundException("Cash advance not found.");
            await CashAdvanceGuard.EnsureNotInPayrollAsync(db, req.Id, c);

            db.CashAdvances.Remove(advance);
            await db.SaveChangesAsync(c);
            return 0;
        }, ct);
}
