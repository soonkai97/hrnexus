using MediatR;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Commands;

public record DeleteStaffEntryCommand(Guid EntryId) : IRequest;

public class DeleteStaffEntryHandler(IAppDbContext db)
    : IRequestHandler<DeleteStaffEntryCommand>
{
    public async Task Handle(DeleteStaffEntryCommand req, CancellationToken ct)
    {
        var entry = await db.StaffEntries.FindAsync([req.EntryId], ct)
            ?? throw new KeyNotFoundException("Staff entry not found.");

        await PayrollLockGuard.EnsureDateOpenAsync(db, entry.EntryDate, ct);

        entry.Delete();
        await db.SaveChangesAsync(ct);
    }
}
