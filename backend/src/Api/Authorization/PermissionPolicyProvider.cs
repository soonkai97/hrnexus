using HRNexus.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HRNexus.Api.Authorization;

/// <summary>
/// Turns any policy named "perm:&lt;key&gt;" (e.g. [Authorize(Policy = "perm:staff.manage")]) into a
/// <see cref="PermissionRequirement"/> on the fly, so we don't have to register a named policy per
/// permission. Non-permission policy names fall through to the default provider.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(AppConstants.Permissions.PolicyPrefix, StringComparison.Ordinal))
        {
            var key = policyName[AppConstants.Permissions.PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(key))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }
}
