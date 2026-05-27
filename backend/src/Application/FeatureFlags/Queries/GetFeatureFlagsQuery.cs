using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.FeatureFlags.Queries;

public record GetFeatureFlagsQuery : IRequest<Dictionary<string, bool>>;

public class GetFeatureFlagsHandler(IAppDbContext db, IMemoryCache cache)
    : IRequestHandler<GetFeatureFlagsQuery, Dictionary<string, bool>>
{
    private const string CacheKey = "feature-flags";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<Dictionary<string, bool>> Handle(
        GetFeatureFlagsQuery request, CancellationToken ct)
    {
        // Cache for 60 s to prevent thundering-herd on startup when dozens of clients
        // all call /api/feature-flags at the same moment. Absolute expiry means flag
        // changes take effect within a minute without any extra invalidation logic.
        return await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var rows = await db.FeatureFlags.ToListAsync(ct);
            return rows.ToDictionary(f => f.Key, f => f.IsEnabled);
        }) ?? new Dictionary<string, bool>();
    }
}
