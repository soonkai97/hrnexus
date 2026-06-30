using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.CashAdvance.Commands;
using HRNexus.Application.CashAdvance.Queries;
using HRNexus.Domain.Constants;
using System.Security.Claims;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/cash-advances")]
[Authorize]
public class CashAdvancesController(IMediator mediator) : ControllerBase
{
    private Guid   CallerId   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CallerName => User.FindFirstValue("fullName") ?? "Unknown";

    private const string ManagePolicy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.CashAdvanceManage;

    /// <summary>Admin: list all cash advances, optionally filtered by status (outstanding | settled).</summary>
    [HttpGet]
    [Authorize(Policy = ManagePolicy)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
        => Ok(await mediator.Send(new GetCashAdvancesQuery(status), ct));

    [HttpPost]
    [Authorize(Policy = ManagePolicy)]
    public async Task<IActionResult> Create([FromBody] CreateCashAdvanceBody body, CancellationToken ct)
    {
        try
        {
            var id = await mediator.Send(
                new CreateCashAdvanceCommand(body.EmployeeId, body.Amount, body.AdvanceDate, body.Reason, CallerName), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)     { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)  { return NotFound(new { error = ex.Message }); }
    }

    [HttpPatch("{id:guid}/settled")]
    [Authorize(Policy = ManagePolicy)]
    public async Task<IActionResult> SetSettled(Guid id, [FromBody] SetSettledBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new SetCashAdvanceSettledCommand(id, body.Settled), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ManagePolicy)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new DeleteCashAdvanceCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }
}

public record CreateCashAdvanceBody(Guid EmployeeId, decimal Amount, DateOnly AdvanceDate, string? Reason);
public record SetSettledBody(bool Settled);
