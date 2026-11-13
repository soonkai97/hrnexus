using HRNexus.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Common;

/// <summary>
/// Resolves the hourly rate that was in effect for a role on a given (possibly back-dated) work
/// date, from the effective-dated <c>RoleRates</c> history — so a shift is priced as-of when it was
/// worked, not at the role's current rate.
/// </summary>
public static class RoleRateLookup
{
    /// <summary>
    /// The rate row with the greatest <c>EffectiveOn</c> &lt;= <paramref name="workDate"/> for the
    /// role. Falls back to <paramref name="currentRate"/> (the role's live rate) when no history row
    /// applies — e.g. a date earlier than any recorded rate, or a role with no history yet.
    /// </summary>
    public static async Task<decimal> RateAsOfAsync(
        IAppDbContext db, Guid roleId, DateOnly workDate, decimal currentRate, CancellationToken ct)
    {
        var rate = await db.RoleRates
            .Where(r => r.RoleId == roleId && r.EffectiveOn <= workDate)
            .OrderByDescending(r => r.EffectiveOn)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);
        return rate ?? currentRate;
    }
}
