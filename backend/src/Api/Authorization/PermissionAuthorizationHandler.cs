using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Api.Authorization;

/// <summary>
/// Satisfies a <see cref="PermissionRequirement"/> by resolving the caller's effective permissions
/// (DB-backed, cached) and checking the required key. Authoritative source — not the JWT claims.
/// </summary>
public sealed class PermissionAuthorizationHandler(IPermissionResolver resolver)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var userId) &&
            await resolver.HasAsync(userId, requirement.PermissionKey))
        {
            context.Succeed(requirement);
        }
    }
}
