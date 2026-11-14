using System.Security.Claims;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Staff.Queries;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize]
public class StaffController(IMediator mediator) : ControllerBase
{
    private Guid   CallerId   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool    includeHidden = false,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 10,
        CancellationToken   ct       = default)
    {
        var result = await mediator.Send(
            new GetStaffListQuery(search, IncludeHidden: includeHidden, Page: page, PageSize: pageSize), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new GetStaffByIdQuery(id), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> Register([FromBody] RegisterStaffCommand cmd, CancellationToken ct)
    {
        try
        {
            var id = await mediator.Send(cmd, ct);
            return CreatedAtAction(nameof(List), new { }, new { id });
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateException ex)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            if (inner.Contains("Email",    StringComparison.OrdinalIgnoreCase)) return Conflict(new { error = "This email is already registered." });
            if (inner.Contains("StaffId",  StringComparison.OrdinalIgnoreCase)) return Conflict(new { error = "This Staff ID is already in use." });
            return BadRequest(new { error = "Failed to save staff record. " + inner });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new UpdateStaffCommand(id, body.StaffId, body.FirstName, body.LastName, body.Email,
                body.Roles, body.PhoneNumber,
                body.IcNo, body.PassportNo, body.DateOfBirth), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateException ex)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            if (inner.Contains("Email",    StringComparison.OrdinalIgnoreCase)) return Conflict(new { error = "This email is already in use." });
            if (inner.Contains("StaffId",  StringComparison.OrdinalIgnoreCase)) return Conflict(new { error = "This Staff ID is already in use." });
            return BadRequest(new { error = "Failed to save staff record. " + inner });
        }
    }

    [HttpPatch("{id}/activate")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        if (id == CallerId)
            return BadRequest(new { error = "You cannot change your own active status." });
        try
        {
            await mediator.Send(new ActivateStaffCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPatch("{id}/deactivate")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        if (id == CallerId)
            return BadRequest(new { error = "You cannot deactivate your own account." });
        try
        {
            await mediator.Send(new DeactivateStaffCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPatch("{id:guid}/hidden")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> SetHidden(Guid id, [FromBody] SetHiddenBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new SetStaffHiddenCommand(id, body.Hidden), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (id == CallerId)
            return BadRequest(new { error = "You cannot delete your own account." });
        try
        {
            await mediator.Send(new DeleteStaffCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new AdminResetPasswordCommand(id, body.NewPassword), ct);
            return NoContent();
        }
        catch (ArgumentException ex)    { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // --- Bulk actions (act on a selected set of staff at once) ---

    [HttpPost("bulk/roles")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> BulkAssignRoles([FromBody] BulkAssignRolesBody body, CancellationToken ct)
    {
        try
        {
            var count = await mediator.Send(new BulkAssignRolesCommand(body.StaffIds, body.Roles), ct);
            return Ok(new { count });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("bulk/deactivate")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> BulkDeactivate([FromBody] BulkDeactivateBody body, CancellationToken ct)
    {
        if (body.StaffIds.Contains(CallerId))
            return BadRequest(new { error = "You cannot deactivate your own account." });
        try
        {
            var count = await mediator.Send(new BulkDeactivateStaffCommand(body.StaffIds), ct);
            return Ok(new { count });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // --- Role management ---

    [HttpGet("roles")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffView)]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        var result = await mediator.Send(new GetRolesQuery(), ct);
        return Ok(result);
    }

    [HttpPost("roles")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.RolesManage)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new CreateRoleCommand(body.Name), ct);
            return NoContent();
        }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPatch("roles/{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.RolesManage)]
    public async Task<IActionResult> RenameRole(Guid id, [FromBody] RenameRoleBody body,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound(new { error = "Role not found." });
        var newName = (body.Name ?? "").Trim().ToUpperInvariant();
        if (newName.Length == 0)
            return BadRequest(new { error = "Role name cannot be empty." });
        if (newName != role.Name && await db.Roles.AnyAsync(r => r.Id != id && r.Name == newName, ct))
            return Conflict(new { error = $"A role named '{newName}' already exists." });
        role.SetName(newName);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("roles/{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.RolesManage)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new DeleteRoleCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)        { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex)   { return Conflict(new { error = ex.Message }); }
    }
}

public record CreateRoleBody(string Name);
public record RenameRoleBody(string? Name);
public record UpdateStaffBody(string StaffId, string FirstName, string? LastName, string? Email, List<Guid> Roles, string? PhoneNumber, string? IcNo, string? PassportNo, DateTime? DateOfBirth);
public record ResetPasswordBody(string NewPassword);
public record BulkAssignRolesBody(List<Guid> StaffIds, List<Guid> Roles);
public record BulkDeactivateBody(List<Guid> StaffIds);
public record SetHiddenBody(bool Hidden);
