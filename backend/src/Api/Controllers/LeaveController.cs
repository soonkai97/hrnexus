using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NightclubWorkforce.Application.Admin.Commands;
using NightclubWorkforce.Application.Admin.Queries;
using NightclubWorkforce.Application.Leave.Commands;
using NightclubWorkforce.Application.Notifications.Commands;
using NightclubWorkforce.Application.Notifications.Queries;

namespace NightclubWorkforce.Api.Controllers;

/// <summary>Employee-facing leave endpoints (authenticated, any role).</summary>
[ApiController]
[Route("api/leave")]
[Authorize]
public class LeaveController(IMediator mediator) : ControllerBase
{
    private Guid EmployeeId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get all leave requests for the current employee (flat list, no pagination).</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        // Use a large page size to return all records — staff don't need pagination on their own history
        var result = await mediator.Send(new GetLeaveQuery(EmployeeId, null, null, null, null, 1, 200), ct);
        return Ok(result.Items);
    }

    /// <summary>Apply for leave. Status is always set to pending.</summary>
    [HttpPost]
    public async Task<IActionResult> Apply([FromBody] ApplyLeaveBody body, CancellationToken ct)
    {
        // Employees cannot create 'no-show' records — that is admin-only
        if (body.LeaveType.Equals("no-show", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        try
        {
            var id = await mediator.Send(new UpsertLeaveCommand(
                null, EmployeeId, body.LeaveType,
                body.StartDate, body.EndDate,
                body.Notes, "pending"), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)        { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>Cancel a pending leave (only the owner can cancel).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new CancelLeaveCommand(id, EmployeeId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)        { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex)   { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Get leave balance for the current employee for a given year.</summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance([FromQuery] int? year, CancellationToken ct)
    {
        var result = await mediator.Send(new GetLeaveBalanceQuery(EmployeeId, year), ct);
        return Ok(result);
    }

}

public record ApplyLeaveBody(
    string   LeaveType,
    DateTime StartDate,
    DateTime EndDate,
    string?  Notes);
