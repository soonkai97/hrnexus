using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Auth.Commands;
using HRNexus.Application.Staff.Queries;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize(Roles = AppConstants.Roles.ManagerOrAdminClaim)]
public class StaffController(IMediator mediator) : ControllerBase
{
    private string CallerRole => User.FindFirstValue(ClaimTypes.Role) ?? "";
    private Guid   CallerId   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>True when the caller is a Manager but NOT an Admin.</summary>
    private bool IsManagerCaller =>
        User.IsInRole(AppConstants.Roles.ManagerClaim) && !User.IsInRole(AppConstants.Roles.AdminClaim);

    private static bool HasElevatedRole(IEnumerable<string> roles) =>
        roles.Any(r => r.Equals(AppConstants.Roles.Manager, StringComparison.OrdinalIgnoreCase) ||
                       r.Equals(AppConstants.Roles.Admin,   StringComparison.OrdinalIgnoreCase));

    private static bool HasAdminRole(IEnumerable<string> roles) =>
        roles.Any(r => r.Equals(AppConstants.Roles.Admin, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the staff member with the given ID has the admin role.</summary>
    private static async Task<bool> IsAdminAsync(Guid id, IAppDbContext db, CancellationToken ct)
    {
        return await db.StaffRoles
            .AnyAsync(r => r.EmployeeId == id && r.Role == AppConstants.Roles.Admin, ct);
    }

    [HttpGet]
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
    public async Task<IActionResult> Register([FromBody] RegisterStaffCommand cmd, CancellationToken ct)
    {
        if (HasAdminRole(cmd.Roles))
            return Forbid();

        if (CallerRole == AppConstants.Roles.ManagerClaim && HasElevatedRole(cmd.Roles))
            return Forbid();

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
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffBody body,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (HasAdminRole(body.Roles))
            return Forbid();

        if (IsManagerCaller && HasElevatedRole(body.Roles))
            return Forbid();

        if (IsManagerCaller && await IsAdminAsync(id, db, ct))
            return Forbid();

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
    [Authorize(Roles = AppConstants.Roles.ManagerOrAdminClaim)]
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
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordBody body,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (IsManagerCaller && await IsAdminAsync(id, db, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(body.NewPassword) || body.NewPassword.Length < AppConstants.Limits.PasswordMinLength)
            return BadRequest(new { error = $"Password must be at least {AppConstants.Limits.PasswordMinLength} characters." });

        try
        {
            await mediator.Send(new AdminResetPasswordCommand(id, body.NewPassword), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // --- Role management ---

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        var result = await mediator.Send(new GetRolesQuery(), ct);
        return Ok(result);
    }

    [HttpPost("roles")]
    [Authorize(Roles = AppConstants.Roles.ManagerOrAdminClaim)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new CreateRoleCommand(body.Name, body.HourlyRate), ct);
            return NoContent();
        }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPatch("roles/{name}/rate")]
    [Authorize(Roles = AppConstants.Roles.ManagerOrAdminClaim)]
    public async Task<IActionResult> UpdateRoleRate(string name, [FromBody] UpdateRoleRateBody body,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (body.HourlyRate < 0)
            return BadRequest(new { error = "Hourly rate cannot be negative." });
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name.ToUpperInvariant(), ct);
        if (role is null) return NotFound(new { error = "Role not found." });
        role.SetRate(body.HourlyRate);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("roles/{name}")]
    [Authorize(Roles = AppConstants.Roles.ManagerOrAdminClaim)]
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

public record CreateRoleBody(string Name, decimal HourlyRate = 0);
public record UpdateRoleRateBody(decimal HourlyRate);
public record UpdateStaffBody(string StaffId, string FirstName, string LastName, string? Email, List<string> Roles, string? PhoneNumber, string? IcNo, string? PassportNo, DateTime? DateOfBirth);
public record ResetPasswordBody(string NewPassword);
