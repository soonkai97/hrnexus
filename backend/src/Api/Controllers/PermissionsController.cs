using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.Permissions.Commands;
using HRNexus.Application.Permissions.Queries;

namespace HRNexus.Api.Controllers;

/// <summary>
/// Manage the permission catalog, role-default permissions, and per-user overrides.
/// Catalog + role endpoints require roles.manage; per-user override endpoints require staff.manage.
/// Validation/not-found errors are mapped to 400/404 by the global ExceptionMiddleware.
/// </summary>
[ApiController]
[Route("api/permissions")]
public class PermissionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "perm:roles.manage")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
        => Ok(await mediator.Send(new GetPermissionCatalogQuery(), ct));

    [HttpGet("roles/{roleName}")]
    [Authorize(Policy = "perm:roles.manage")]
    public async Task<IActionResult> GetRole(string roleName, CancellationToken ct)
        => Ok(await mediator.Send(new GetRolePermissionsQuery(roleName), ct));

    [HttpPut("roles/{roleName}")]
    [Authorize(Policy = "perm:roles.manage")]
    public async Task<IActionResult> SetRole(string roleName, [FromBody] SetRolePermissionsBody body, CancellationToken ct)
    {
        await mediator.Send(new SetRolePermissionsCommand(roleName, body.Permissions ?? []), ct);
        return NoContent();
    }

    [HttpGet("users/{employeeId:guid}")]
    [Authorize(Policy = "perm:staff.manage")]
    public async Task<IActionResult> GetUser(Guid employeeId, CancellationToken ct)
        => Ok(await mediator.Send(new GetStaffOverridesQuery(employeeId), ct));

    [HttpPut("users/{employeeId:guid}")]
    [Authorize(Policy = "perm:staff.manage")]
    public async Task<IActionResult> SetUser(Guid employeeId, [FromBody] SetStaffOverridesBody body, CancellationToken ct)
    {
        await mediator.Send(new SetStaffOverridesCommand(employeeId, body.Overrides ?? []), ct);
        return NoContent();
    }

    public record SetRolePermissionsBody(List<string>? Permissions);
    public record SetStaffOverridesBody(List<StaffOverrideDto>? Overrides);
}
