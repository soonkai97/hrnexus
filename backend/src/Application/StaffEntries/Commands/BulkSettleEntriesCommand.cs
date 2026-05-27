using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Commands;

public record BulkSettleEntriesCommand(List<Guid> EntryIds, string Settlement) : IRequest<int>;

public class BulkSettleEntriesHandler(IAppDbContext db)
    : IRequestHandler<BulkSettleEntriesCommand, int>
{
    public async Task<int> Handle(BulkSettleEntriesCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Settlement))
            throw new ArgumentException("Settlement is required.");

        var entries = await db.StaffEntries
            .Where(e => req.EntryIds.Contains(e.Id) && !e.IsDeleted)
            .ToListAsync(ct);

        foreach (var entry in entries)
            entry.Settle(req.Settlement);

        await db.SaveChangesAsync(ct);
        return entries.Count;
    }
}
