using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Admin.Queries;
using HRNexus.Domain.Constants;
using System.Security.Claims;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(IMediator mediator) : ControllerBase
{
    private Guid   CallerId   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    // The JWT carries the display name in a custom "fullName" claim (see TokenService), not ClaimTypes.Name.
    private string CallerName => User.FindFirstValue("fullName") ?? "Unknown";
    // ── Dashboard ─────────────────────────────────────────────────────────────

    [HttpGet("dashboard")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.ReportsView)]
    public async Task<IActionResult> Dashboard(CancellationToken ct) =>
        Ok(await mediator.Send(new GetDashboardQuery(), ct));

    // ── Leave ─────────────────────────────────────────────────────────────────

    [HttpGet("leave")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveApprove)]
    public async Task<IActionResult> GetLeave(
        [FromQuery] Guid?     employeeId = null,
        [FromQuery] DateTime? dateFrom   = null,
        [FromQuery] DateTime? dateTo     = null,
        [FromQuery] string?   status     = null,
        [FromQuery] string?   leaveType  = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 10,
        CancellationToken ct = default)
    {
        if (pageSize > AppConstants.Limits.MaxPageSize) pageSize = AppConstants.Limits.MaxPageSize;
        var result = await mediator.Send(
            new GetLeaveQuery(employeeId, dateFrom, dateTo, status, leaveType, page, pageSize), ct);
        return Ok(result);
    }

    [HttpPost("leave")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveApprove)]
    public async Task<IActionResult> CreateLeave([FromBody] UpsertLeaveBody body, CancellationToken ct)
    {
        try
        {
            var id = await mediator.Send(new UpsertLeaveCommand(
                null, body.EmployeeId, body.LeaveType,
                body.StartDate, body.EndDate,
                body.StartTime, body.EndTime, body.Hours,
                body.Notes, body.Status ?? "approved", body.RejectionReason,
                SkipAdvanceNotice: true), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)        { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPut("leave/{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveApprove)]
    public async Task<IActionResult> UpdateLeave(Guid id, [FromBody] UpsertLeaveBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new UpsertLeaveCommand(
                id, body.EmployeeId, body.LeaveType,
                body.StartDate, body.EndDate,
                body.StartTime, body.EndTime, body.Hours,
                body.Notes, body.Status ?? "approved", body.RejectionReason), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("leave/{id:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveApprove)]
    public async Task<IActionResult> DeleteLeave(Guid id, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new DeleteLeaveCommand(id), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ── Leave Balance ─────────────────────────────────────────────────────────

    [HttpGet("leave-balance")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveManage)]
    public async Task<IActionResult> GetAllLeaveBalances(
        [FromQuery] int? year = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAllLeaveBalancesQuery(year), ct);
        return Ok(result);
    }

    [HttpGet("leave-balance/{employeeId:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveManage)]
    public async Task<IActionResult> GetLeaveBalance(Guid employeeId,
        [FromQuery] int? year = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLeaveBalanceQuery(employeeId, year), ct);
        return Ok(result);
    }

    [HttpPut("leave-balance/{employeeId:guid}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveManage)]
    public async Task<IActionResult> SetLeaveBalance(Guid employeeId,
        [FromBody] LeaveEntitlementBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new UpsertLeaveEntitlementCommand(
                employeeId, body.Year, body.AnnualHours, body.SickHours), ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Set the same entitlement for all active staff for a given year.</summary>
    [HttpPost("leave-balance/bulk")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveManage)]
    public async Task<IActionResult> BulkSetLeaveBalance([FromBody] BulkLeaveEntitlementBody body, CancellationToken ct)
    {
        try
        {
            var count = await mediator.Send(
                new BulkUpsertLeaveEntitlementCommand(body.Year, body.AnnualHours, body.SickHours), ct);
            return Ok(new { count });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Carry over unused annual leave from one year to the next for all active staff.</summary>
    [HttpPost("leave-balance/carry-over")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveManage)]
    public async Task<IActionResult> CarryOverLeave([FromBody] CarryOverBody body, CancellationToken ct)
    {
        try
        {
            var count = await mediator.Send(
                new CarryOverLeaveEntitlementCommand(body.FromYear, body.ToYear, body.MaxCarryOver ?? 14m), ct);
            return Ok(new { count });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Bulk approve or reject a list of pending leave requests.</summary>
    [HttpPost("leave/bulk-status")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveApprove)]
    public async Task<IActionResult> BulkUpdateLeaveStatus(
        [FromBody] BulkUpdateLeaveStatusBody body, CancellationToken ct)
    {
        try
        {
            var count = await mediator.Send(
                new BulkUpdateLeaveStatusCommand(body.Ids, body.Status, body.RejectionReason), ct);
            return Ok(new { count });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Bulk-mark a list of leave requests as "no-show" (rostered but did not turn up).</summary>
    [HttpPost("leave/bulk-no-show")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveManage)]
    public async Task<IActionResult> BulkMarkLeaveNoShow(
        [FromBody] BulkMarkLeaveNoShowBody body, CancellationToken ct)
    {
        try
        {
            var count = await mediator.Send(new BulkMarkLeaveNoShowCommand(body.Ids), ct);
            return Ok(new { count });
        }
        catch (ArgumentException ex)         { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>Aggregated leave usage summary for all active staff for a given year.</summary>
    [HttpGet("leave/summary")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveApprove)]
    public async Task<IActionResult> GetLeaveSummary(
        [FromQuery] int? year = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLeaveSummaryQuery(year), ct);
        return Ok(result);
    }

    /// <summary>Staff with frequent sick leave (3+ requests in rolling 90 days).</summary>
    [HttpGet("leave/pattern-warnings")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LeaveApprove)]
    public async Task<IActionResult> GetLeavePatternWarnings(CancellationToken ct) =>
        Ok(await mediator.Send(new GetLeavePatternWarningsQuery(), ct));

}

public record UpsertLeaveBody(Guid EmployeeId, string LeaveType, DateTime StartDate, DateTime EndDate, string? StartTime, string? EndTime, decimal? Hours, string? Notes, string? Status, string? RejectionReason = null);
public record BulkUpdateLeaveStatusBody(List<Guid> Ids, string Status, string? RejectionReason = null);
public record BulkMarkLeaveNoShowBody(List<Guid> Ids);
public record LeaveEntitlementBody(int Year, decimal AnnualHours, decimal SickHours);
public record BulkLeaveEntitlementBody(int Year, decimal AnnualHours, decimal SickHours);
public record CarryOverBody(int FromYear, int ToYear, decimal? MaxCarryOver);
