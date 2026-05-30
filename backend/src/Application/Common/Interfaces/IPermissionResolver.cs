namespace HRNexus.Application.Common.Interfaces;

/// <summary>
/// Resolves a user's EFFECTIVE permission set = (union of their roles' default permissions)
/// plus per-user granted overrides, minus per-user revoked overrides (revoke wins). Results are
/// cached in-process and invalidated when role/user permissions change, so the DB stays
/// authoritative and revocation takes effect immediately (not bound to the long JWT lifetime).
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    Task<bool> HasAsync(Guid userId, string permissionKey, CancellationToken ct = default);

    /// <summary>Drop one user's cached permissions (after editing their per-user overrides).</summary>
    void Invalidate(Guid userId);

    /// <summary>Drop ALL cached permissions (after editing a role's default permissions, which can
    /// affect many users at once).</summary>
    void InvalidateAll();
}
