using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.Messaging.Commands;
using HRNexus.Application.Messaging.Queries;
using HRNexus.Api.Filters;
using HRNexus.Domain.Constants;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/messages")]
[Authorize]
[FeatureRequired("messagingAdmin", "messagingStaff")]
public class MessagingController(IMediator mediator) : ControllerBase
{
    private Guid   CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    // Display name lives in the custom "fullName" claim (see TokenService) — case-sensitive.
    private string CurrentName   => User.FindFirstValue("fullName")
        ?? User.FindFirstValue(ClaimTypes.Email) ?? "Staff";
    private bool   IsAdmin => User.IsInRole(AppConstants.Roles.AdminClaim) || User.IsInRole(AppConstants.Roles.ManagerClaim);

    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(CancellationToken ct) =>
        Ok(await mediator.Send(new GetInboxQuery(CurrentUserId, IsAdmin), ct));

    [HttpGet("sent")]
    public async Task<IActionResult> Sent(CancellationToken ct) =>
        Ok(await mediator.Send(new GetSentQuery(CurrentUserId), ct));

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Content) || body.Content.Length > 2000)
            return BadRequest(new { error = "Content must be 1–2000 characters." });
        var id = await mediator.Send(new SendMessageCommand(CurrentUserId, CurrentName, IsAdmin,
            body.RecipientId, body.RecipientName, body.Content), ct);
        return Ok(new { id });
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await mediator.Send(new MarkMessageReadCommand(id, CurrentUserId), ct);
        return NoContent();
    }
}

public record SendMessageBody(Guid? RecipientId, string? RecipientName, string Content);
