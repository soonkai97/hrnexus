using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Queries;

public record GetOutstandingEntriesQuery : IRequest<List<StaffOutstandingDto>>;

public record StaffOutstandingDto(
    string  EntryId,
    string  StaffId,
    string  StaffCode,
    string  StaffName,
    string  EntryDate,
    string  WorkType,
    decimal VenueEarnings,
    decimal PrivateNet,
    string  Settlement);

public class GetOutstandingEntriesHandler(IAppDbContext db)
    : IRequestHandler<GetOutstandingEntriesQuery, List<StaffOutstandingDto>>
{
    public async Task<List<StaffOutstandingDto>> Handle(GetOutstandingEntriesQuery req, CancellationToken ct)
    {
        var entries = await db.StaffEntries
            .Where(e => !e.IsDeleted
                     && (e.Settlement == "PENDING" || e.Settlement == "NOTCOLLECTED"))
            .OrderBy(e => e.EntryDate)
            .ToListAsync(ct);

        var staffIds  = entries.Select(e => e.StaffId).Distinct().ToList();
        var staffDict = await db.Staff
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id,
                s => new { s.StaffId, Name = (s.FirstName + " " + s.LastName).Trim() }, ct);

        return entries
            .Select(e =>
            {
                staffDict.TryGetValue(e.StaffId, out var info);
                return new StaffOutstandingDto(
                    e.Id.ToString(),
                    e.StaffId.ToString(),
                    info?.StaffId ?? "",
                    info?.Name    ?? "Unknown",
                    e.EntryDate.ToString("yyyy-MM-dd"),
                    e.WorkType,
                    e.VenueEarnings,
                    e.PrivateNet,
                    e.Settlement);
            })
            .ToList();
    }
}
