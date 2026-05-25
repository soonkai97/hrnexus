using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Admin.Queries;

public record GetTipsPoolsQuery(DateTime? DateFrom, DateTime? DateTo) : IRequest<List<TipsPoolDto>>;

public record TipsPoolDto(
    Guid     Id,
    string   LocationName,
    DateTime ShiftDate,
    decimal  TotalAmount,
    string   SplitMethod,
    bool     IsDistributed,
    int      StaffCount,
    DateTime CreatedAt);

public class GetTipsPoolsHandler(IAppDbContext db)
    : IRequestHandler<GetTipsPoolsQuery, List<TipsPoolDto>>
{
    public async Task<List<TipsPoolDto>> Handle(GetTipsPoolsQuery req, CancellationToken ct)
    {
        var query = db.TipsPools.AsQueryable();

        if (req.DateFrom.HasValue)
            query = query.Where(t => t.ShiftDate >= req.DateFrom.Value.Date);

        if (req.DateTo.HasValue)
            query = query.Where(t => t.ShiftDate <= req.DateTo.Value.Date);

        return await query
            .OrderByDescending(t => t.ShiftDate)
            .Select(t => new TipsPoolDto(
                t.Id,
                t.LocationName,
                t.ShiftDate,
                t.TotalAmount,
                t.SplitMethod,
                t.IsDistributed,
                t.StaffCount,
                t.CreatedAt))
            .ToListAsync(ct);
    }
}
