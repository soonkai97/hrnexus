using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NightclubWorkforce.Application.Announcements.Commands;
using NightclubWorkforce.Application.Announcements.Queries;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Api.Filters;

namespace NightclubWorkforce.Api.Controllers;

[ApiController]
[Route("api/announcements")]
[Authorize]
[FeatureRequired("announcements")]
public class AnnouncementsController(IMediator mediator, IAppDbContext db) : ControllerBase
{
    private Guid EmployeeId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CallerRole => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    // ── Staff endpoints ────────────────────────────────────────────────────────

    /// <summary>Get active announcements visible to the current employee's role(s).</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (!roles.Any()) roles.Add(CallerRole);

        var result = await mediator.Send(new GetAnnouncementsQuery(EmployeeId, roles), ct);
        return Ok(result);
    }

    /// <summary>Mark an announcement as read for the current employee.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await mediator.Send(new MarkAnnouncementReadCommand(id, EmployeeId), ct);
        return NoContent();
    }

    // ── Admin / Manager endpoints ──────────────────────────────────────────────

    /// <summary>Get all announcements with read-receipt counts (admin view).</summary>
    [HttpGet("all")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct     = default)
    {
        var result = await mediator.Send(new GetAllAnnouncementsQuery(page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Create a new announcement.</summary>
    [HttpPost]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Create([FromBody] UpsertAnnouncementRequest req, CancellationToken ct)
    {
        var creatorId = EmployeeId;
        var creator = await db.Staff.FindAsync([creatorId], ct);
        var creatorName = creator?.FullName ?? "Admin";

        var id = await mediator.Send(new UpsertAnnouncementCommand(
            null, req.Title, req.Body, req.TargetRoles, req.ExpiresAt,
            creatorId, creatorName), ct);

        return CreatedAtAction(nameof(GetMine), new { }, new { id });
    }

    /// <summary>Update an existing announcement.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertAnnouncementRequest req, CancellationToken ct)
    {
        try
        {
            var creatorId = EmployeeId;
            var creator = await db.Staff.FindAsync([creatorId], ct);
            var creatorName = creator?.FullName ?? "Admin";

            await mediator.Send(new UpsertAnnouncementCommand(
                id, req.Title, req.Body, req.TargetRoles, req.ExpiresAt,
                creatorId, creatorName), ct);

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>Delete an announcement (hard delete).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new DeleteAnnouncementCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}

public record UpsertAnnouncementRequest(
    string    Title,
    string    Body,
    string    TargetRoles,
    DateTime? ExpiresAt);
