using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Queries;

public record GetStaffEntriesQuery(Guid StaffId, int Year, int Month) : IRequest<List<StaffEntryDto>>;

public record StaffEntryDto(
    string   Id,
    string   StaffId,
    string   EntryDate,
    string   WorkType,
    decimal  VenueUnits,
    decimal  VenueRate,
    decimal  VenueEarnings,
    decimal  PrivateGross,
    decimal  PrivateNet,
    string   Settlement,
    string?  Notes,
    DateTime  CreatedAt,
    DateTime? ModifiedAt);

public class GetStaffEntriesHandler(IAppDbContext db)
    : IRequestHandler<GetStaffEntriesQuery, List<StaffEntryDto>>
{
    public async Task<List<StaffEntryDto>> Handle(GetStaffEntriesQuery req, CancellationToken ct) =>
        await db.StaffEntries
            .Where(e => e.StaffId == req.StaffId
                     && e.EntryDate.Year  == req.Year
                     && e.EntryDate.Month == req.Month)
            .OrderBy(e => e.EntryDate)
            .Select(e => new StaffEntryDto(
                e.Id.ToString(), e.StaffId.ToString(), e.EntryDate.ToString("yyyy-MM-dd"),
                e.WorkType, e.VenueUnits, e.VenueRate, e.VenueEarnings,
                e.PrivateGross, e.PrivateNet, e.Settlement, e.Notes,
                e.CreatedAt, e.ModifiedAt))
            .ToListAsync(ct);
}
