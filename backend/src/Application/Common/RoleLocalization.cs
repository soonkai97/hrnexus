using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Common;

/// <summary>
/// Resolves a WorkLog's frozen <c>RoleName</c> snapshot (UPPERCASE) back to the job's CURRENT
/// localized display names (Chinese / Vietnamese) for read-side display only. The snapshot stays the
/// record-of-truth; this just lets the UI show the job in the viewer's language. A name that no longer
/// matches any role (deleted or renamed job) resolves to (null, null) so the client falls back to the
/// snapshot — which for a renamed job is correctly the name the shift was actually logged under.
/// </summary>
public sealed class RoleLocalization
{
    private readonly Dictionary<string, (string? Zh, string? Vi)> _byName;

    private RoleLocalization(Dictionary<string, (string? Zh, string? Vi)> byName) => _byName = byName;

    public (string? Zh, string? Vi) For(string roleName) =>
        _byName.TryGetValue(roleName, out var v) ? v : (null, null);

    /// <summary>Builds a lookup for exactly the role names present in <paramref name="roleNames"/>.</summary>
    public static async Task<RoleLocalization> BuildAsync(IAppDbContext db, IEnumerable<string> roleNames, CancellationToken ct)
    {
        var names = roleNames.Distinct().ToList();
        if (names.Count == 0)
            return new RoleLocalization(new());

        var map = await db.Roles
            .Where(r => names.Contains(r.Name))
            .Select(r => new { r.Name, r.NameZh, r.NameVi })
            .ToDictionaryAsync(r => r.Name, r => ((string?)r.NameZh, (string?)r.NameVi), ct);

        return new RoleLocalization(map);
    }
}
