using HRNexus.Application.Policy;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRNexus.Api.Controllers;

/// <summary>
/// The company's HR policy: working week, leave limits, carry-over rule. Anyone signed in may READ it (both
/// apps preview leave hours with the working week); only policy.manage may change it.
/// </summary>
[ApiController]
[Route("api/company-policy")]
[Authorize]
public class CompanyPolicyController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await mediator.Send(new GetCompanyPolicyQuery(), ct));

    [HttpPut]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PolicyManage)]
    public async Task<IActionResult> Update([FromBody] UpdateCompanyPolicyBody body, CancellationToken ct)
    {
        try
        {
            var version = await mediator.Send(new UpdateCompanyPolicyCommand(
                body.WorkingDays ?? [], body.WorkdayHours, body.AnnualLeaveAdvanceDays, body.MaxLeaveDays,
                body.CarryOverMode ?? "", body.CarryOverMaxHours, body.UnpaidLeaveDivisor, body.Version), ct);
            return Ok(new { version });
        }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

public record UpdateCompanyPolicyBody(int[]? WorkingDays, decimal WorkdayHours, int AnnualLeaveAdvanceDays, int MaxLeaveDays,
    string? CarryOverMode, decimal CarryOverMaxHours, decimal UnpaidLeaveDivisor, int? Version);
