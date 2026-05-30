using Microsoft.AspNetCore.Authorization;

namespace HRNexus.Api.Authorization;

/// <summary>Authorization requirement carrying the permission key a request must hold.</summary>
public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}
