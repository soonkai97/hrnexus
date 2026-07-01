using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Common;

/// <summary>
/// Resolves a WorkLog's frozen <c>LocationName</c>/<c>LocationId</c> snapshot back to the location's
/// CURRENT localized display names (Chinese / Vietnamese) for read-side display only. The snapshot stays
/// the record-of-truth; this just lets the UI show the location in the viewer's language. A key that no
/// longer matches any location (deleted location) resolves to (null, null) so the client falls back to
/// the snapshot. Two builders are provided so callers can key by whichever they have: the exact
/// <c>LocationId</c> (preferred) or, when only the frozen name is grouped, the <c>Name</c>.
/// </summary>
public sealed class LocationLocalization
{
    private readonly Dictionary<string, (string? Zh, string? Vi)> _byKey;

    private LocationLocalization(Dictionary<string, (string? Zh, string? Vi)> byKey) => _byKey = byKey;

    public (string? Zh, string? Vi) For(string key) =>
        _byKey.TryGetValue(key, out var v) ? v : (null, null);

    /// <summary>Builds a lookup keyed by location <c>Id</c> for exactly the ids in <paramref name="ids"/>.</summary>
    public static async Task<LocationLocalization> BuildByIdsAsync(IAppDbContext db, IEnumerable<string> ids, CancellationToken ct)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
            return new LocationLocalization(new());

        var map = await db.Locations
            .Where(l => keys.Contains(l.Id))
            .Select(l => new { l.Id, l.NameZh, l.NameVi })
            .ToDictionaryAsync(l => l.Id, l => ((string?)l.NameZh, (string?)l.NameVi), ct);

        return new LocationLocalization(map);
    }

    /// <summary>Builds a lookup keyed by location <c>Name</c> for exactly the names in <paramref name="names"/>.</summary>
    public static async Task<LocationLocalization> BuildByNamesAsync(IAppDbContext db, IEnumerable<string> names, CancellationToken ct)
    {
        var keys = names.Distinct().ToList();
        if (keys.Count == 0)
            return new LocationLocalization(new());

        var map = await db.Locations
            .Where(l => keys.Contains(l.Name))
            .Select(l => new { l.Name, l.NameZh, l.NameVi })
            .ToDictionaryAsync(l => l.Name, l => ((string?)l.NameZh, (string?)l.NameVi), ct);

        return new LocationLocalization(map);
    }
}
