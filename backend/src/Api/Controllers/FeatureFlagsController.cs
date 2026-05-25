using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.FeatureFlags.Commands;
using HRNexus.Application.FeatureFlags.Queries;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/feature-flags")]
[Authorize]
public class FeatureFlagsController(IMediator mediator, IFeatureMgmtService featureMgmt) : ControllerBase
{
    /// <summary>Returns all feature flags. Any authenticated user can read them.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await mediator.Send(new GetFeatureFlagsQuery(), ct));

    /// <summary>
    /// One-time helper: hash a raw PIN so you can paste it into FeatureMgmt:PinHash.
    /// Only works before the PIN is configured (returns 404 once a hash is set).
    /// </summary>
    [HttpPost("hash-pin")]
    [Authorize(Roles = "Admin")]
    public IActionResult HashPin([FromBody] HashPinRequest req)
    {
        if (featureMgmt.IsPinConfigured)
            return NotFound(new { error = "PIN is already configured." });

        if (string.IsNullOrWhiteSpace(req.Pin) || req.Pin.Length < 8)
            return BadRequest(new { error = "PIN must be at least 8 characters." });

        return Ok(new { hash = featureMgmt.HashPin(req.Pin) });
    }

    /// <summary>
    /// Validates the access PIN and returns a short-lived unlock token (4 hours).
    /// Rate-limited to 5 attempts per IP before a 15-minute lockout.
    /// </summary>
    [HttpPost("unlock")]
    [Authorize(Roles = "Admin")]
    public IActionResult Unlock([FromBody] UnlockRequest req)
    {
        if (!featureMgmt.IsPinConfigured)
            return StatusCode(503, new { error = "Feature management PIN is not configured on this server." });

        var ip     = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = featureMgmt.TryUnlock(req.Pin, ip);

        return result.Status switch
        {
            UnlockStatus.Success   => Ok(new { token = result.Token, expiresAt = result.ExpiresAt }),
            UnlockStatus.LockedOut => StatusCode(429, new { error = "Too many failed attempts. Try again later.", retryAfter = result.RetryAfter }),
            _                      => Unauthorized(new { error = "Invalid PIN." }),
        };
    }

    /// <summary>
    /// Replaces all feature flags. Admin only + requires a valid feature-access unlock token.
    /// </summary>
    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(
        [FromHeader(Name = "X-Feature-Access")] string? accessToken,
        [FromBody] Dictionary<string, bool> flags,
        CancellationToken ct)
    {
        if (!featureMgmt.ValidateToken(accessToken))
            return StatusCode(403, new { error = "Feature management access token is missing or expired. Please re-enter your PIN." });

        await mediator.Send(new UpdateFeatureFlagsCommand(flags), ct);
        return NoContent();
    }
}

public record HashPinRequest(string Pin);
public record UnlockRequest(string Pin);
