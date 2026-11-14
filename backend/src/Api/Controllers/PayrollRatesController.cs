using HRNexus.Application.Payroll.Commands;
using HRNexus.Application.Payroll.Queries;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRNexus.Api.Controllers;

/// <summary>
/// The statutory rate tables payroll calculates with. Anyone with <c>payroll.view</c> can read them. Marking a row
/// verified, or switching an optional HRD Corp row on or off, needs its own permission (<c>payroll.rates.verify</c>),
/// so the person who prepares payroll cannot also sign off the rates that gate its approval. Rates, rounding and
/// bands can never be edited through the API: a correction is a new row.
/// </summary>
[ApiController]
[Route("api/payroll/rates")]
public class PayrollRatesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollView)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await mediator.Send(new GetStatutoryRatesQuery(), ct));

    [HttpPost("{id:guid}/verify")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollRatesVerify)]
    public async Task<IActionResult> Verify(Guid id, [FromBody] VerifyRateBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new VerifyStatutoryRateCommand(id, body.Note ?? ""), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollRatesVerify)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetRateActiveBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new SetStatutoryRateActiveCommand(id, body.Active), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

public record VerifyRateBody(string? Note);
public record SetRateActiveBody(bool Active);
