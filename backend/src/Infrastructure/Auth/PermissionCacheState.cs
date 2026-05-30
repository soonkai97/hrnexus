namespace HRNexus.Infrastructure.Auth;

/// <summary>
/// Process-wide generation counter for the permission cache (registered as a singleton).
/// IMemoryCache can't enumerate keys, so a role-permission change that affects many
/// users invalidates everything at once by bumping this counter — it's folded into every cache key,
/// so all prior-generation entries become unreachable and expire via their TTL.
/// </summary>
public sealed class PermissionCacheState
{
    private int _generation;
    public int Generation => Volatile.Read(ref _generation);
    public void Bump() => Interlocked.Increment(ref _generation);
}
