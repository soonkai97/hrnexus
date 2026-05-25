using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Auth.Commands;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Staff.Queries;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IMediator mediator) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand cmd, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(cmd, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand cmd, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(cmd, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordBody body, CancellationToken ct)
    {
        var employeeId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            await mediator.Send(new ChangePasswordCommand(employeeId, body.CurrentPassword, body.NewPassword), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)        { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)           { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var employeeId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var result = await mediator.Send(new GetStaffByIdQuery(employeeId), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPatch("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileBody body, CancellationToken ct)
    {
        var employeeId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new UpdateProfileCommand(employeeId, body.PhoneNumber), ct);
        return NoContent();
    }

    [HttpGet("language")]
    [Authorize]
    public async Task<IActionResult> GetLanguage(
        [FromServices] HRNexus.Application.Common.Interfaces.IAppDbContext db,
        CancellationToken ct)
    {
        var employeeId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var lang = await db.Staff
            .Where(e => e.Id == employeeId)
            .Select(e => e.PreferredLanguage)
            .FirstOrDefaultAsync(ct);
        return Ok(new { language = lang ?? "en" });
    }

    [HttpPatch("language")]
    [Authorize]
    public async Task<IActionResult> UpdateLanguage([FromBody] UpdateLanguageBody body, CancellationToken ct)
    {
        var employeeId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            await mediator.Send(new UpdateLanguageCommand(employeeId, body.Language), ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordBody body, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new ForgotPasswordCommand(body.StaffId), ct);
            return Ok(new
            {
                message = result.Found
                    ? "Your request has been sent to your manager. They will contact you with a new password."
                    : "Staff ID not found. Please check and try again.",
                found = result.Found,
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

}

public record ChangePasswordBody(string CurrentPassword, string NewPassword);
public record UpdateProfileBody(string? PhoneNumber);
public record UpdateLanguageBody(string Language);
public record ForgotPasswordBody(string StaffId);
