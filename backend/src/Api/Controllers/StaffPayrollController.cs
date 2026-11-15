using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands;
using HRNexus.Application.Payroll.Commands.Run;
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
public class StaffPayrollController(IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    private const string ViewPolicy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollView;
    private const string ManagePolicy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PayrollManage;

    /// <summary>The acting person is always the signed-in user; no request body can name someone else.</summary>
    private PayrollActor Actor() => new(
        currentUser.UserId ?? throw new UnauthorizedAccessException("Sign in to continue."),
        currentUser.UserName ?? "Unknown");

    // ── recurring allowances and deductions ──────────────────────────────────────

    [HttpGet("pay-items")]
    [Authorize(Policy = ViewPolicy)]
    public async Task<IActionResult> GetPayItems(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetPayItemsQuery(id, Actor().Id), ct));

    [HttpPost("pay-items")]
    [Authorize(Policy = ManagePolicy)]
    public async Task<IActionResult> AddPayItem(Guid id, [FromBody] AddPayItemBody body, CancellationToken ct)
    {
        var itemId = await mediator.Send(new AddPayItemCommand(id, body.Kind, body.Reason ?? "", body.Amount, body.CountsForEpf,
            body.CountsForSocsoEis, body.Priority, body.From, body.To, Actor()), ct);
        return Created($"api/staff/{id}/pay-items", new { id = itemId });
    }

    [HttpPut("pay-items/{itemId:guid}/end")]
    [Authorize(Policy = ManagePolicy)]
    public async Task<IActionResult> EndPayItem(Guid id, Guid itemId, [FromBody] EndPayItemBody body, CancellationToken ct)
    {
        await mediator.Send(new EndPayItemCommand(id, itemId, body.To, Actor()), ct);
        return NoContent();
    }

    [HttpDelete("pay-items/{itemId:guid}")]
    [Authorize(Policy = ManagePolicy)]
    public async Task<IActionResult> DeletePayItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await mediator.Send(new DeletePayItemCommand(id, itemId, Actor()), ct);
        return NoContent();
    }

    // ── salary and employment ────────────────────────────────────────────────────

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

public record AddPayItemBody(string Kind, string? Reason, decimal Amount, bool CountsForEpf, bool CountsForSocsoEis, int Priority, DateOnly From, DateOnly? To);
public record EndPayItemBody(DateOnly To);

public record AddSalaryBody(decimal BasicSalary, string PayFrequency, DateOnly EffectiveFrom,
    string? BankName, string? BankAccount, string? BankHolder);

public record EmploymentBody(string CitizenshipStatus, DateOnly StartDate, DateOnly? EndDate);
