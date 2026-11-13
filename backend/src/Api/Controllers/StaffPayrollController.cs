using HRNexus.Application.Payroll.Commands;
using HRNexus.Application.Payroll.Queries;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRNexus.Api.Controllers;

/// <summary>
/// A person's pay: salary history (append-only) and the employment details payroll needs. Salary is
/// only ever returned from here, never from the staff endpoints, so it stays masked for everyone
/// without a payroll permission (PAY-09).
/// </summary>
[ApiController]
[Route("api/staff/{id:guid}")]
public class StaffPayrollController(IMediator mediator) : ControllerBase
{
    [HttpGet("salary")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollView)]
    public async Task<IActionResult> GetSalary(Guid id, CancellationToken ct)
    {
        try { return Ok(await mediator.Send(new GetSalaryQuery(id), ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("salary")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollManage)]
    public async Task<IActionResult> AddSalary(Guid id, [FromBody] AddSalaryBody body, CancellationToken ct)
    {
        try
        {
            var profileId = await mediator.Send(new AddSalaryProfileCommand(
                id, body.BasicSalary, body.PayFrequency, body.EffectiveFrom,
                body.BankName, body.BankAccount, body.BankHolder), ct);
            return Created($"api/staff/{id}/salary", new { id = profileId });
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPut("employment")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollManage)]
    public async Task<IActionResult> SetEmployment(Guid id, [FromBody] EmploymentBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new SetEmploymentDetailsCommand(id, body.CitizenshipStatus, body.StartDate, body.EndDate), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)    { return BadRequest(new { error = ex.Message }); }
    }
}

public record AddSalaryBody(decimal BasicSalary, string PayFrequency, DateOnly EffectiveFrom,
    string? BankName, string? BankAccount, string? BankHolder);

public record EmploymentBody(string CitizenshipStatus, DateOnly StartDate, DateOnly? EndDate);
