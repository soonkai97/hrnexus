using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.StaffEntries.Commands;

public record CreateStaffEntryCommand(
    Guid    StaffId,
    string  EntryDate,
    string  WorkType,
    decimal VenueUnits,
    decimal VenueRate,
    decimal VenueEarnings,
    decimal PrivateGross,
    decimal PrivateNet,
    string  Settlement,
    string? Notes) : IRequest<Guid>;

public class CreateStaffEntryHandler(IAppDbContext db)
    : IRequestHandler<CreateStaffEntryCommand, Guid>
{
    public async Task<Guid> Handle(CreateStaffEntryCommand req, CancellationToken ct)
    {
        if (!await db.Staff.AnyAsync(s => s.Id == req.StaffId, ct))
            throw new KeyNotFoundException("Staff member not found.");

        if (!DateOnly.TryParse(req.EntryDate, out var date))
            throw new ArgumentException("Invalid date format. Use yyyy-MM-dd.");

        var entry = StaffEntry.Create(
            req.StaffId, date, req.WorkType,
            req.VenueUnits, req.VenueRate, req.VenueEarnings,
            req.PrivateGross, req.PrivateNet,
            req.Settlement, req.Notes);

        db.StaffEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry.Id;
    }
}
