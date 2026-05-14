using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.FeatureFlags.Queries;

public record GetFeatureFlagsQuery : IRequest<Dictionary<string, bool>>;

public class GetFeatureFlagsHandler(IAppDbContext db)
    : IRequestHandler<GetFeatureFlagsQuery, Dictionary<string, bool>>
{
    public async Task<Dictionary<string, bool>> Handle(
        GetFeatureFlagsQuery request, CancellationToken ct)
    {
        var rows = await db.FeatureFlags.ToListAsync(ct);
        return rows.ToDictionary(f => f.Key, f => f.IsEnabled);
    }
}
