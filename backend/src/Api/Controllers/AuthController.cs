using System.Security.Claims;
using HRNexus.Application.Auth.Commands;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Staff.Queries;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]   // per-IP fixed-window flood protection (see Program.cs)
public class AuthController(IMediator mediator) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand cmd, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(cmd, ct);
            SetAuthCookies(result.AccessToken, result.RefreshToken);
            // Tokens live ONLY in httpOnly cookies — never in the body (XSS can't read them).
            return Ok(new { user = result.User, expiresIn = result.ExpiresIn });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>Rotates the session using the refresh token carried in the httpOnly cookie.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AppConstants.AuthCookies.RefreshToken];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { error = "No refresh token." });

        try
        {
            var result = await mediator.Send(new RefreshTokenCommand(refreshToken), ct);
            SetAuthCookies(result.AccessToken, result.RefreshToken);
            return Ok(new { user = result.User, expiresIn = result.ExpiresIn });
        }
        catch (UnauthorizedAccessException ex)
        {
            // Stale / already-used refresh token — clear the cookies so the client stops retrying.
            ClearAuthCookies();
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>Clears the session: expires the auth cookies and invalidates the server-side
    /// refresh token. Works even with an expired access token (no [Authorize]) — the refresh
    /// cookie identifies the session to revoke.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AppConstants.AuthCookies.RefreshToken];
        if (!string.IsNullOrEmpty(refreshToken))
            await mediator.Send(new LogoutCommand(refreshToken), ct);
        ClearAuthCookies();
        return NoContent();
    }

    /// <summary>Effective permission keys for the signed-in user — drives FE guards/UI gating.</summary>
    [HttpGet("me/permissions")]
    [Authorize]
    public async Task<IActionResult> GetMyPermissions(CancellationToken ct)
        => Ok(await mediator.Send(new HRNexus.Application.Permissions.Queries.GetMyPermissionsQuery(), ct));


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

    // ── Auth-cookie helpers ────────────────────────────────────────────────────
    // HttpOnly        → JS (and therefore XSS) cannot read the token.
    // SameSite=Strict → the cookie is never sent on cross-site requests, which is our CSRF defense
    //                   (the SPA is same-origin with the API via the nginx proxy).
    // Secure          → only over HTTPS. Request.IsHttps is accurate behind nginx because
    //                   X-Forwarded-Proto is honoured (see Program.cs ForwardedHeaders), so this is
    //                   true in prod (https) and false on the local http stack — both correct.
    private CookieOptions CookieOpts(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure   = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path     = "/",
        Expires  = expires,
    };

    private void SetAuthCookies(string accessToken, string refreshToken)
    {
        // Persistent (survives browser restart) to preserve the existing 30-day "stay signed in"
        // UX. The JWT's own exp claim — not the cookie lifetime — is the real access gate.
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        Response.Cookies.Append(AppConstants.AuthCookies.AccessToken, accessToken, CookieOpts(expires));
        Response.Cookies.Append(AppConstants.AuthCookies.RefreshToken, refreshToken, CookieOpts(expires));
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete(AppConstants.AuthCookies.AccessToken, CookieOpts());
        Response.Cookies.Delete(AppConstants.AuthCookies.RefreshToken, CookieOpts());
    }
}

public record UpdateProfileBody(string? PhoneNumber);
public record ForgotPasswordBody(string StaffId);
