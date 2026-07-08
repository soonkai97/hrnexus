using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.Common;

/// <summary>
/// Resolves the per-job maximum shift length (in days) for a shift-capture flow. Commands that have
/// the <c>Role</c> in hand read <c>role.MaxShiftDays</c> directly; the clock-out / admin-close paths
/// only carry the role-NAME snapshot on the work log, so they name-join back to <c>Roles</c> here.
/// </summary>
public static class RoleShiftLimit
{
    /// <summary>
    /// The <c>MaxShiftDays</c> for the job whose (uppercase) name matches <paramref name="roleName"/>.
    /// Falls back to <see cref="AppConstants.WorkLog.MaxShiftDays"/> when the job can't be resolved
    /// (e.g. a renamed or deleted job) so the sanity guard is never lost.
    /// </summary>
    public static async Task<int> MaxDaysByNameAsync(
        IAppDbContext db, string roleName, CancellationToken ct)
    {
        var days = await db.Roles
            .Where(r => r.Name == roleName)
            .Select(r => (int?)r.MaxShiftDays)
            .FirstOrDefaultAsync(ct);
        return days ?? AppConstants.WorkLog.MaxShiftDays;
    }
}
