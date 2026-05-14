using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NightclubWorkforce.Application.Push;

namespace NightclubWorkforce.Api.Controllers;

[ApiController]
[Route("api/push")]
[Authorize]
public class PushController(IMediator mediator, IConfiguration config) : ControllerBase
{
    private Guid EmployeeId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Return the VAPID public key so the PWA can subscribe.</summary>
    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    public IActionResult GetPublicKey()
        => Ok(new { publicKey = config["WebPush:PublicKey"] ?? string.Empty });

    /// <summary>Store a browser push subscription for the current user.</summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscribeRequest req, CancellationToken ct)
    {
        await mediator.Send(new SavePushSubscriptionCommand(EmployeeId, req.Endpoint, req.P256dh, req.Auth), ct);
        return Ok();
    }

    /// <summary>Remove a browser push subscription for the current user.</summary>
    [HttpDelete("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] PushUnsubscribeRequest req, CancellationToken ct)
    {
        await mediator.Send(new RemovePushSubscriptionCommand(EmployeeId, req.Endpoint), ct);
        return Ok();
    }
}

public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);
public record PushUnsubscribeRequest(string Endpoint);
