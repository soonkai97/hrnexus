using MediatR;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Commands;

public record UpdateStaffEntryCommand(
    Guid    EntryId,
    string  EntryDate,
    string  WorkType,
    decimal VenueUnits,
    decimal VenueRate,
    decimal VenueEarnings,
    decimal PrivateGross,
    decimal PrivateNet,
    string  Settlement,
    string? Notes) : IRequest;

public class UpdateStaffEntryHandler(IAppDbContext db)
    : IRequestHandler<UpdateStaffEntryCommand>
{
    public async Task Handle(UpdateStaffEntryCommand req, CancellationToken ct)
    {
        var entry = await db.StaffEntries.FindAsync([req.EntryId], ct)
            ?? throw new KeyNotFoundException("Staff entry not found.");

        if (!DateOnly.TryParse(req.EntryDate, out var date))
            throw new ArgumentException("Invalid date format. Use yyyy-MM-dd.");

        entry.Update(date, req.WorkType,
            req.VenueUnits, req.VenueRate, req.VenueEarnings,
            req.PrivateGross, req.PrivateNet,
            req.Settlement, req.Notes);

        await db.SaveChangesAsync(ct);
    }
}
