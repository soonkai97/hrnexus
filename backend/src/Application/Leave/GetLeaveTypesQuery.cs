using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Leave;

/// <summary>The company's leave types. Staff need only the active ones; the settings page also needs the inactive ones.</summary>
/// <param name="IncludeInactive">Include deactivated types.</param>
/// <param name="WithUsage">Mark each type that already has leave (the settings page locks its pool and day counting).</param>
public record GetLeaveTypesQuery(bool IncludeInactive = false, bool WithUsage = false) : IRequest<LeaveTypeCatalogDto>;

/// <param name="Version">Counts saves of the catalogue (0 = nothing saved yet, so the code defaults apply); a save made from a stale copy is refused.</param>
/// <param name="IsSaved">False while the code defaults apply.</param>
/// <param name="Types">The types, in sort order.</param>
public record LeaveTypeCatalogDto(int Version, bool IsSaved, IReadOnlyList<LeaveTypeDto> Types);

public record LeaveTypeDto(
    string   Key,
    string   Label,
    string   Colour,
    int      SortOrder,
    bool     Active,
    bool     IsSystem,
    string   Pool,
    decimal? AllowanceDays,
    decimal? MaxDaysPerRequest,
    bool     AutoApprove,
    int?     AdvanceNoticeDays,
    bool     AdvanceNoticeFromPolicy,
    bool     DeductsPay,
    bool     AdminOnly,
    bool     CountsCalendarDays,
    bool     Sensitive,
    bool     HasLeave)
{
    public static LeaveTypeDto From(LeaveTypeDefinition t, bool hasLeave) => new(
        t.Key, t.Label, t.Colour, t.SortOrder, t.Active, t.IsSystem, t.Pool, t.AllowanceDays, t.MaxDaysPerRequest,
        t.AutoApprove, t.AdvanceNoticeDays, t.AdvanceNoticeFromPolicy, t.DeductsPay, t.AdminOnly,
        t.CountsCalendarDays, t.Sensitive, hasLeave);
}

public class GetLeaveTypesQueryHandler(IAppDbContext db) : IRequestHandler<GetLeaveTypesQuery, LeaveTypeCatalogDto>
{
    public async Task<LeaveTypeCatalogDto> Handle(GetLeaveTypesQuery req, CancellationToken ct)
    {
        var catalog = await LeaveCatalogReader.GetAsync(db, ct);
        var used = req.WithUsage
            ? (await db.LeaveRequests.Select(l => l.LeaveType).Distinct().ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var types = catalog.Types
            .Where(t => req.IncludeInactive || t.Active)
            .Select(t => LeaveTypeDto.From(t, used.Contains(t.Key)))
            .ToList();
        return new LeaveTypeCatalogDto(catalog.IsSaved ? catalog.Types.Max(t => t.CatalogVersion) : 0, catalog.IsSaved, types);
    }
}
