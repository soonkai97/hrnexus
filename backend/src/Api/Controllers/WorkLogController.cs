using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.WorkLog.Commands;
using HRNexus.Application.WorkLog.Queries;
using HRNexus.Domain.Constants;
using System.Security.Claims;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/worklogs")]
[Authorize]
public class WorkLogController(IMediator mediator) : ControllerBase
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
            var id = await mediator.Send(new StartWorkLogCommand(CallerId, req.RoleId, req.LocationId, req.StartTime, req.Notes, req.Latitude, req.Longitude), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)          { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>Staff pushes a fresh GPS fix for their current shift ("Update location" button).</summary>
    [HttpPatch("active/location")]
    public async Task<IActionResult> UpdateActiveLocation([FromBody] UpdateLocationRequest req, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new UpdateMyShiftLocationCommand(CallerId, req.Latitude, req.Longitude), ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("entry")]
    public async Task<IActionResult> ManualEntry([FromBody] ManualWorkLogRequest req, CancellationToken ct)
    {
        try
        {
            var id = await mediator.Send(new CreateManualWorkLogCommand(CallerId, req.RoleId, req.LocationId, req.WorkDate, req.StartTime, req.EndTime, req.Notes), ct);
            return Ok(new { id });
        }
        catch (ArgumentException ex)      { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)   { return NotFound(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkLogRequest req, CancellationToken ct)
    {
        var isPrivileged = User.IsInRole(AppConstants.Roles.AdminClaim) || User.IsInRole(AppConstants.Roles.ManagerClaim);
        try
        {
            await mediator.Send(new UpdateWorkLogCommand(id, CallerId, isPrivileged, req.RoleId, req.LocationId, req.WorkDate, req.StartTime, req.EndDate, req.EndTime, req.Notes), ct);
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

    // Admin/manager: close an OPEN shift (staff forgot to clock out) at a chosen end time.
    [HttpPut("{id:guid}/admin-end")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.WorkLogManage)]
    public async Task<IActionResult> AdminEnd(Guid id, [FromBody] AdminEndShiftRequest req, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new AdminEndShiftCommand(id, CallerId, req.EndDate, req.EndTime), ct);
            return NoContent();
        }
        catch (ArgumentException ex)          { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            var isPrivileged = User.IsInRole(AppConstants.Roles.AdminClaim) || User.IsInRole(AppConstants.Roles.ManagerClaim);
            await mediator.Send(new DeleteWorkLogCommand(id, CallerId, isPrivileged), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    // Admin: get all work logs (paginated)
    [HttpGet]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.WorkLogManage)]
    public async Task<IActionResult> GetAll([FromQuery] GetAllWorkLogsParams p, CancellationToken ct)
    {
        var pageSize = Math.Min(p.PageSize, AppConstants.Limits.MaxPageSize);
        var result = await mediator.Send(new GetAllWorkLogsQuery(p.From, p.To, p.EmployeeId, p.Page, pageSize), ct);
        return Ok(result);
    }
}

public record StartWorkLogRequest(Guid RoleId, string LocationId, string StartTime, string? Notes, double? Latitude = null, double? Longitude = null);
public record ManualWorkLogRequest(Guid RoleId, string LocationId, DateOnly WorkDate, string StartTime, string EndTime, string? Notes);
public record UpdateWorkLogRequest(Guid RoleId, string LocationId, DateOnly WorkDate, string StartTime, DateOnly EndDate, string EndTime, string? Notes);
public record EndWorkLogRequest(string EndTime);
public record UpdateLocationRequest(double Latitude, double Longitude);
public record AdminEndShiftRequest(DateOnly EndDate, string EndTime);
public record GetAllWorkLogsParams(DateTime? From, DateTime? To, Guid? EmployeeId, int Page = 1, int PageSize = 10);
