using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions;

namespace HRNexus.Application.Payroll;

/// <summary>
/// Re-checks, in the database and without any cache, that a person holds a payroll permission right now (active account,
/// role grant plus per-user grant minus per-user revoke). The API's policy decides from a cache that can lag a few minutes
/// behind a deactivation or a role change; pay figures must stop being readable the moment access is withdrawn.
/// </summary>
public static class PayrollAccess
{
    public static async Task RequireAsync(IAppDbContext db, string permissionKey, Guid userId, CancellationToken ct,
        string message = "You are not allowed to see this.")
    {
        var holders = await PermissionHolders.GetIdsAsync(db, permissionKey, ct);
        if (!holders.Contains(userId)) throw new ForbiddenException(message);
    }
}
