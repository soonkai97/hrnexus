using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Infrastructure.Auth;

/// <summary>
/// DB-backed, in-process-cached implementation of <see cref="IPermissionResolver"/>. Effective set:
/// (union of role-default permissions for the user's roles) + granted overrides - revoked overrides.
/// A deactivated user resolves to no permissions. Cached for a short TTL keyed by userId + a
/// generation counter; <see cref="Invalidate"/> drops one user, <see cref="InvalidateAll"/> bumps
/// the generation. Mirrors the caching approach in GetFeatureFlagsHandler.
/// </summary>
public sealed class PermissionResolver(IAppDbContext db, IMemoryCache cache, PermissionCacheState state)
    : IPermissionResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        var cached = await cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await ComputeAsync(userId, ct);
        });
        return cached ?? new HashSet<string>();
    }

    public async Task<bool> HasAsync(Guid userId, string permissionKey, CancellationToken ct = default)
        => (await GetEffectivePermissionsAsync(userId, ct)).Contains(permissionKey);

    public void Invalidate(Guid userId) => cache.Remove(CacheKey(userId));

    public void InvalidateAll() => state.Bump();

    private string CacheKey(Guid userId) => $"perm:{state.Generation}:{userId}";

    private async Task<IReadOnlySet<string>> ComputeAsync(Guid userId, CancellationToken ct)
    {
        // A deactivated account has no permissions, regardless of role/overrides.
        var isActive = await db.Staff
            .Where(s => s.Id == userId)
            .Select(s => s.IsActive)
            .FirstOrDefaultAsync(ct);
        if (!isActive) return new HashSet<string>();

        var roleIds = await db.StaffRoles
            .Where(r => r.EmployeeId == userId)
            .Select(r => r.RoleId)
            .ToListAsync(ct);

        var effective = new HashSet<string>(StringComparer.Ordinal);

        if (roleIds.Count > 0)
        {
            var rolePerms = await db.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionKey)
                .ToListAsync(ct);
            foreach (var key in rolePerms) effective.Add(key);
        }

        var overrides = await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == userId)
            .Select(o => new { o.PermissionKey, o.Granted })
            .ToListAsync(ct);
        // Grants first, then revokes — so a revoke always wins over a role default or a grant.
        foreach (var o in overrides) { if (o.Granted) effective.Add(o.PermissionKey); }
        foreach (var o in overrides) { if (!o.Granted) effective.Remove(o.PermissionKey); }

        return effective;
    }
}
