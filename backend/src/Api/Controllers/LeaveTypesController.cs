using HRNexus.Application.Leave;
using HRNexus.Application.Leave.Commands;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Leave;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRNexus.Api.Controllers;

/// <summary>
/// The company's leave types (Phase 7q). Anyone signed in reads the catalogue: the apply forms, labels and
/// balances are built from it, and a deactivated type stays listed (flagged inactive) so old leave keeps its
/// name. Only policy.manage reads the usage figures the editor needs, and changes the catalogue.
/// </summary>
[ApiController]
[Route("api/leave-types")]
[Authorize]
public class LeaveTypesController(IMediator mediator) : ControllerBase
{
    /// <summary>Every leave type in sort order with its active flag (clients offer only the active ones).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await mediator.Send(new GetLeaveTypesQuery(IncludeInactive: true), ct));

    /// <summary>The same plus which types already have leave (the editor locks their pool) for the settings page.</summary>
    [HttpGet("all")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PolicyManage)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await mediator.Send(new GetLeaveTypesQuery(IncludeInactive: true, WithUsage: true), ct));

    /// <summary>Replaces the whole catalogue. A stale version is refused (409).</summary>
    [HttpPut]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.PolicyManage)]
    public async Task<IActionResult> Save([FromBody] SaveLeaveTypesBody body, CancellationToken ct)
    {
        try
        {
            var specs = (body.Types ?? []).Select(t => new LeaveTypeSpec(
                t.Key ?? "", t.Label ?? "", t.Colour ?? "", t.SortOrder, t.Active, t.Pool ?? "", t.AllowanceDays,
                t.MaxDaysPerRequest, t.AutoApprove, t.AdvanceNoticeDays, t.AdvanceNoticeFromPolicy, t.DeductsPay,
                t.AdminOnly, t.CountsCalendarDays, t.Sensitive)).ToList();
            var version = await mediator.Send(new SaveLeaveTypesCommand(body.Version, specs), ct);
            return Ok(new { version });
        }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

public record SaveLeaveTypesBody(int Version, List<LeaveTypeBody>? Types);

public record LeaveTypeBody(
    string?  Key,
    string?  Label,
    string?  Colour,
    int      SortOrder,
    bool     Active,
    string?  Pool,
    decimal? AllowanceDays,
    decimal? MaxDaysPerRequest,
    bool     AutoApprove,
    int?     AdvanceNoticeDays,
    bool     AdvanceNoticeFromPolicy,
    bool     DeductsPay,
    bool     AdminOnly,
    bool     CountsCalendarDays,
    bool     Sensitive);
