using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.StaffEntries.Queries;

public record GetLedgerSummaryQuery(int Year, int Month) : IRequest<List<StaffLedgerPayoutDto>>;

public record StaffLedgerPayoutDto(
    string  StaffId,
    string  StaffCode,
    string  StaffName,
    decimal VenueEarnings,
    decimal PrivateNet,
    decimal NetPayout,
    int     EntryCount,
    int     PendingCount,
    int     NotCollectedCount);

public class GetLedgerSummaryHandler(IAppDbContext db)
    : IRequestHandler<GetLedgerSummaryQuery, List<StaffLedgerPayoutDto>>
{
    public async Task<List<StaffLedgerPayoutDto>> Handle(GetLedgerSummaryQuery req, CancellationToken ct)
    {
        var grouped = await db.StaffEntries
            .Where(e => !e.IsDeleted
                     && e.EntryDate.Year  == req.Year
                     && e.EntryDate.Month == req.Month)
            .GroupBy(e => e.StaffId)
            .Select(g => new
            {
                StaffId           = g.Key,
                VenueEarnings     = g.Sum(e => e.VenueEarnings),
                PrivateNet        = g.Sum(e => e.PrivateNet),
                EntryCount        = g.Count(),
                PendingCount      = g.Count(e => e.Settlement == "PENDING"),
                NotCollectedCount = g.Count(e => e.Settlement == "NOTCOLLECTED"),
            })
            .ToListAsync(ct);

        var staffIds  = grouped.Select(g => g.StaffId).ToList();
        var staffDict = await db.Staff
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id,
                s => new { s.StaffId, Name = (s.FirstName + " " + s.LastName).Trim() }, ct);

        return grouped
            .Select(g =>
            {
                staffDict.TryGetValue(g.StaffId, out var info);
                return new StaffLedgerPayoutDto(
                    g.StaffId.ToString(),
                    info?.StaffId ?? "",
                    info?.Name    ?? "Unknown",
                    g.VenueEarnings,
                    g.PrivateNet,
                    g.VenueEarnings + g.PrivateNet,
                    g.EntryCount,
                    g.PendingCount,
                    g.NotCollectedCount);
            })
            .OrderBy(r => r.StaffCode)
            .ToList();
    }
}
