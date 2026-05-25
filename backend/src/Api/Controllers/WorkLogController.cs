using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Commands;
using HRNexus.Application.WorkLog.Queries;
using HRNexus.Domain.Constants;
using System.Security.Claims;
using HRNexus.Api.Filters;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/worklogs")]
[Authorize]
public class WorkLogController(IMediator mediator, IDateTimeProvider dt) : ControllerBase
{
    private Guid CallerId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var result = await mediator.Send(new GetActiveWorkLogQuery(CallerId), ct);
        return Ok(result);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMy([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyWorkLogsQuery(CallerId, page, pageSize), ct);
        return Ok(result);
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartWorkLogRequest req, CancellationToken ct)
    {
        try
        {
            var id = await mediator.Send(new StartWorkLogCommand(CallerId, req.RoleName, req.LocationId, req.StartTime, req.Notes), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)          { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("entry")]
    public async Task<IActionResult> ManualEntry([FromBody] ManualWorkLogRequest req, CancellationToken ct)
    {
        try
        {
            var id = await mediator.Send(new CreateManualWorkLogCommand(CallerId, req.RoleName, req.LocationId, req.WorkDate, req.StartTime, req.EndTime, req.Notes), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)      { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)   { return NotFound(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkLogRequest req, CancellationToken ct)
    {
        var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Manager");
        try
        {
            await mediator.Send(new UpdateWorkLogCommand(id, CallerId, isPrivileged, req.RoleName, req.LocationId, req.WorkDate, req.StartTime, req.EndTime, req.Notes), ct);
            return NoContent();
        }
        catch (ArgumentException ex)          { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)       { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex)  { return BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException)   { return Forbid(); }
    }

    [HttpPut("{id:guid}/end")]
    public async Task<IActionResult> End(Guid id, [FromBody] EndWorkLogRequest req, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new EndWorkLogCommand(id, CallerId, req.EndTime), ct);
            return NoContent();
        }
        catch (ArgumentException ex)          { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new DeleteWorkLogCommand(id, CallerId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    /// <summary>Returns a pay summary (hours, pay breakdown, per-shift lines) for the current employee.</summary>
    [HttpGet("pay-summary")]
    [FeatureRequired("paySummary")]
    public async Task<IActionResult> GetPaySummary(
        [FromQuery] DateOnly? start,
        [FromQuery] DateOnly? end,
        CancellationToken ct)
    {
        var today       = DateOnly.FromDateTime(dt.LocalNow);
        var periodStart = start ?? new DateOnly(today.Year, today.Month, 1);
        var periodEnd   = end   ?? today;
        var result = await mediator.Send(new GetPaySummaryQuery(CallerId, periodStart, periodEnd), ct);
        return Ok(result);
    }

    // Admin: get all work logs (paginated)
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetAll([FromQuery] GetAllWorkLogsParams p, CancellationToken ct)
    {
        var pageSize = Math.Min(p.PageSize, AppConstants.Limits.MaxPageSize);
        var result = await mediator.Send(new GetAllWorkLogsQuery(p.From, p.To, p.EmployeeId, p.Page, pageSize), ct);
        return Ok(result);
    }
}

public record StartWorkLogRequest(string RoleName, string LocationId, string StartTime, string? Notes);
public record ManualWorkLogRequest(string RoleName, string LocationId, DateOnly WorkDate, string StartTime, string EndTime, string? Notes);
public record UpdateWorkLogRequest(string RoleName, string LocationId, DateOnly WorkDate, string StartTime, string EndTime, string? Notes);
public record EndWorkLogRequest(string EndTime);
public record GetAllWorkLogsParams(DateTime? From, DateTime? To, Guid? EmployeeId, int Page = 1, int PageSize = 10);
