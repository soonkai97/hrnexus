using Microsoft.AspNetCore.Authorization;

namespace HRNexus.Api.Authorization;

/// <summary>Authorization requirement carrying the permission key (or any-of list of keys) a request must hold.</summary>
public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    /// <summary>One key, or several joined by '|' (any one of them is enough, e.g. "payroll.signoff|payroll.approve").</summary>
    public string PermissionKey { get; } = permissionKey;

    public IReadOnlyList<string> AnyOf { get; } = permissionKey.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
