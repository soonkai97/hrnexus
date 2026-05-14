using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NightclubWorkforce.Application.Notifications.Commands;
using NightclubWorkforce.Application.Notifications.Queries;

namespace NightclubWorkforce.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationController(IMediator mediator) : ControllerBase
{
    private Guid EmployeeId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get latest 50 notifications for the current employee.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] bool unreadOnly = false, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetNotificationsQuery(EmployeeId, unreadOnly), ct);
        return Ok(result);
    }

    /// <summary>Mark a specific notification as read.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await mediator.Send(new MarkNotificationsReadCommand(EmployeeId, id), ct);
        return NoContent();
    }

    /// <summary>Mark all notifications as read.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await mediator.Send(new MarkNotificationsReadCommand(EmployeeId, null), ct);
        return NoContent();
    }
}
