using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Api.Filters;
using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Admin.Queries;
using HRNexus.Domain.Constants;

namespace HRNexus.Api.Controllers;

/// <summary>
/// Manage payroll-period locks. Locking a month freezes its shift logs and ledger records.
/// Manager + admin may lock and reopen.
/// </summary>
[ApiController]
[Route("api/payroll-locks")]
[Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollLock)]
[FeatureRequired("payroll")]
public class PayrollLocksController(IMediator mediator) : ControllerBase
{
    // The JWT carries the display name in a custom "fullName" claim (see TokenService), not ClaimTypes.Name.
    private string CallerName => User.FindFirstValue("fullName") ?? "Unknown";

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? year, CancellationToken ct)
        => Ok(await mediator.Send(new GetPayrollLocksQuery(year), ct));

    [HttpPost]
    public async Task<IActionResult> Lock([FromBody] LockPeriodBody body, CancellationToken ct)
    {
        try
        {
            var id = await mediator.Send(new LockPayrollPeriodCommand(body.Year, body.Month, CallerName, body.Note), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("{year:int}/{month:int}")]
    public async Task<IActionResult> Reopen(int year, int month, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new ReopenPayrollPeriodCommand(year, month, CallerName), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }
}

public record LockPeriodBody(int Year, int Month, string? Note);
