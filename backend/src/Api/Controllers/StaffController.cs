using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Staff.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize]
public class StaffController(IMediator mediator) : ControllerBase
{
    private Guid   CallerId   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Whether the caller is a "restricted" manager. Managers now have the SAME power as admins,
    /// so this is always false — no staff-management action (viewing/editing/deleting admin
    /// accounts, etc.) is withheld from managers.
    /// </summary>
    private static bool IsManagerCaller => false;

    /// <summary>Returns true if the staff member with the given ID has the admin role.</summary>
    private static async Task<bool> IsAdminAsync(Guid id, IAppDbContext db, CancellationToken ct)
    {
        return await db.StaffRoles
            .AnyAsync(r => r.EmployeeId == id && r.Role == AppConstants.Roles.Admin, ct);
    }

    [HttpGet]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 10,
        CancellationToken   ct       = default)
    {
        var result = await mediator.Send(
            new GetStaffListQuery(search, ExcludeAdmins: IsManagerCaller, Page: page, PageSize: pageSize), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffView)]
    public async Task<IActionResult> GetById(Guid id,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (IsManagerCaller && await IsAdminAsync(id, db, ct))
            return Forbid();
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
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffBody body,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (IsManagerCaller)
        {
            // Load the current roles once — used for both the admin check and role preservation
            var currentRoles = await db.StaffRoles
                .Where(r => r.EmployeeId == id)
                .Select(r => r.Role)
                .ToListAsync(ct);

            // Managers can't edit admin staff profiles
            if (currentRoles.Contains(AppConstants.Roles.Admin))
                return Forbid();

            // Managers can't change role assignments — preserve current roles silently
            body = body with { Roles = currentRoles };
        }

        try
        {
            await mediator.Send(new UpdateStaffCommand(id, body.StaffId, body.FirstName, body.LastName, body.Email,
                body.Roles, body.PhoneNumber,
                body.IcNo, body.PassportNo, body.DateOfBirth, body.Locations), ct);
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
    public async Task<IActionResult> Activate(Guid id,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (id == CallerId)
            return BadRequest(new { error = "You cannot change your own active status." });
        if (IsManagerCaller && await IsAdminAsync(id, db, ct))
            return Forbid();
        try
        {
            await mediator.Send(new ActivateStaffCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPatch("{id}/deactivate")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> Deactivate(Guid id,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (id == CallerId)
            return BadRequest(new { error = "You cannot deactivate your own account." });
        if (IsManagerCaller && await IsAdminAsync(id, db, ct))
            return Forbid();
        try
        {
            await mediator.Send(new DeactivateStaffCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> Delete(Guid id,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (id == CallerId)
            return BadRequest(new { error = "You cannot delete your own account." });
        if (await IsAdminAsync(id, db, ct))
            return BadRequest(new { error = "Admin accounts cannot be deleted." });
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
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordBody body,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (IsManagerCaller && await IsAdminAsync(id, db, ct))
            return Forbid();

        try
        {
            await mediator.Send(new AdminResetPasswordCommand(id, body.NewPassword), ct);
            return NoContent();
        }
        catch (ArgumentException ex)    { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // --- Bulk actions (act on a selected set of staff at once) ---

    [HttpPost("bulk/locations")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> BulkAssignLocations([FromBody] BulkAssignLocationsBody body, CancellationToken ct)
    {
        try
        {
            var count = await mediator.Send(new BulkAssignLocationsCommand(body.StaffIds, body.LocationIds), ct);
            return Ok(new { count });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

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

    /// <summary>Import many staff at once (partial success — bad rows are reported and skipped).</summary>
    [HttpPost("bulk-import")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.StaffManage)]
    public async Task<IActionResult> BulkImport([FromBody] BulkRegisterStaffCommand cmd, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(cmd, ct);
            return Ok(result);
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
            await mediator.Send(new CreateRoleCommand(body.Name, body.HourlyRate, body.NameZh, body.NameVi, body.PayType), ct);
            return NoContent();
        }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>Create many custom roles at once (duplicates skipped).</summary>
    [HttpPost("roles/bulk")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.RolesManage)]
    public async Task<IActionResult> BulkCreateRoles([FromBody] BulkCreateRolesCommand cmd, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(cmd, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPatch("roles/{name}/rate")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.RolesManage)]
    public async Task<IActionResult> UpdateRoleRate(string name, [FromBody] UpdateRoleRateBody body,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (body.HourlyRate < 0)
            return BadRequest(new { error = "Hourly rate cannot be negative." });
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name.ToUpperInvariant(), ct);
        if (role is null) return NotFound(new { error = "Role not found." });
        role.SetNameZh(body.NameZh);
        role.SetNameVi(body.NameVi);
        role.SetPayType(body.PayType);
        if (role.HourlyRate != body.HourlyRate)
        {
            role.SetRate(body.HourlyRate);
            // Append a rate-history row effective today. Existing rows are never mutated, so shifts
            // worked before this change keep being priced at the prior rate (as-of their work date).
            db.RoleRates.Add(HRNexus.Domain.Entities.RoleRate.Create(
                role.Name, body.HourlyRate, DateOnly.FromDateTime(DateTime.UtcNow)));
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("roles/{name}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.RolesManage)]
    public async Task<IActionResult> DeleteRole(string name, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new DeleteRoleCommand(name), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)        { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex)   { return Conflict(new { error = ex.Message }); }
    }
}

public record CreateRoleBody(string Name, decimal HourlyRate = 0, string? NameZh = null, string? NameVi = null, string PayType = AppConstants.PayType.Hourly);
public record UpdateRoleRateBody(decimal HourlyRate, string? NameZh = null, string? NameVi = null, string PayType = AppConstants.PayType.Hourly);
public record UpdateStaffBody(string StaffId, string FirstName, string? LastName, string? Email, List<string> Roles, string? PhoneNumber, string? IcNo, string? PassportNo, DateTime? DateOfBirth, List<string>? Locations = null);
public record ResetPasswordBody(string NewPassword);
public record BulkAssignLocationsBody(List<Guid> StaffIds, List<string> LocationIds);
public record BulkAssignRolesBody(List<Guid> StaffIds, List<string> Roles);
public record BulkDeactivateBody(List<Guid> StaffIds);
