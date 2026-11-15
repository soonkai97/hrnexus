using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Application.Payroll.Queries;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRNexus.Api.Controllers;

/// <summary>
/// The monthly payroll run. Reading needs payroll.view (the figures are pay), preparing needs payroll.manage, approving
/// and returning need payroll.approve (the head of HR), signing off a line needs payroll.signoff or payroll.approve,
/// locking needs payroll.lock and reopening payroll.reopen. A manager's own view of their team needs only payroll.signoff.
/// The acting person is always the signed-in user; no request body can name someone else. Errors come back through the
/// exception middleware (400 invalid, 403 not allowed, 404 not found, 409 wrong state or a changed run), and none of them
/// carries an amount.
/// </summary>
[ApiController]
[Route("api/payroll/runs")]
public class PayrollRunsController(IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    private const string Prefix = AppConstants.Permissions.PolicyPrefix;
    private const string View = Prefix + AppConstants.Permissions.PayrollView;
    private const string Manage = Prefix + AppConstants.Permissions.PayrollManage;
    private const string Approve = Prefix + AppConstants.Permissions.PayrollApprove;
    private const string Signoff = Prefix + AppConstants.Permissions.PayrollSignoff;
    private const string SignoffOrApprove = Prefix + AppConstants.Permissions.PayrollSignoff + "|" + AppConstants.Permissions.PayrollApprove;
    private const string Lock = Prefix + AppConstants.Permissions.PayrollLock;
    private const string Reopen = Prefix + AppConstants.Permissions.PayrollReopen;

    private PayrollActor Actor() => new(
        currentUser.UserId ?? throw new UnauthorizedAccessException("Sign in to continue."),
        currentUser.UserName ?? "Unknown");

    // ── reading ──────────────────────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Policy = View)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await mediator.Send(new GetPayrollRunsQuery(Actor().Id), ct));

    /// <summary>One run in full. Add <c>?notInRun=true</c> on a draft to also list who cannot be in it yet (it re-reads the month's inputs).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = View)]
    public async Task<IActionResult> Get(Guid id, [FromQuery] bool notInRun, CancellationToken ct) =>
        Ok(await mediator.Send(new GetPayrollRunQuery(id, Actor().Id, notInRun), ct));

    /// <summary>The submitted-or-later runs that include the signed-in manager's direct reports (no money), newest first.</summary>
    [HttpGet("team")]
    [Authorize(Policy = Signoff)]
    public async Task<IActionResult> TeamRuns(CancellationToken ct) =>
        Ok(await mediator.Send(new GetPayrollTeamRunsQuery(Actor().Id), ct));

    /// <summary>The lines of the people who report to the signed-in user (only theirs, only once the run is submitted).</summary>
    [HttpGet("{id:guid}/team")]
    [Authorize(Policy = Signoff)]
    public async Task<IActionResult> Team(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetPayrollTeamQuery(id, Actor().Id), ct));

    // ── preparing ────────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Policy = Manage)]
    public async Task<IActionResult> Start([FromBody] StartRunBody body, CancellationToken ct)
    {
        var id = await mediator.Send(new StartPayrollRunCommand(body.Year, body.Month, Actor()), ct);
        return Ok(new { id });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeletePayrollRunCommand(id, Actor()), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/recalculate")]
    [Authorize(Policy = Manage)]
    public async Task<IActionResult> Recalculate(Guid id, [FromBody] VersionBody body, CancellationToken ct) =>
        Ok(new { version = await mediator.Send(new RecalculatePayrollRunCommand(id, body.ExpectedVersion, Actor()), ct) });

    [HttpPut("{id:guid}/lines/{employeeId:guid}")]
    [Authorize(Policy = Manage)]
    public async Task<IActionResult> UpdateLine(Guid id, Guid employeeId, [FromBody] UpdateLineBody body, CancellationToken ct) =>
        Ok(new { version = await mediator.Send(new UpdatePayrollRunLineCommand(id, employeeId, body.Pcb, body.Excluded, body.ExcludedReason, body.ExpectedVersion, Actor()), ct) });

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = Manage)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] VersionBody body, CancellationToken ct) =>
        Ok(new { version = await mediator.Send(new SubmitPayrollRunCommand(id, body.ExpectedVersion, Actor()), ct) });

    // ── reviewing ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/lines/{employeeId:guid}/signoff")]
    [Authorize(Policy = SignoffOrApprove)]
    public async Task<IActionResult> SignOff(Guid id, Guid employeeId, [FromBody] VersionBody body, CancellationToken ct) =>
        Ok(new { version = await mediator.Send(new SignOffPayrollLineCommand(id, employeeId, body.ExpectedVersion, Actor()), ct) });

    [HttpPost("{id:guid}/signoff-all")]
    [Authorize(Policy = Approve)]
    public async Task<IActionResult> SignOffAll(Guid id, [FromBody] VersionBody body, CancellationToken ct) =>
        Ok(new { signed = await mediator.Send(new SignOffAllPayrollLinesCommand(id, body.ExpectedVersion, Actor()), ct) });

    [HttpPost("{id:guid}/return")]
    [Authorize(Policy = Approve)]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnBody body, CancellationToken ct) =>
        Ok(new { version = await mediator.Send(new ReturnPayrollRunCommand(id, body.Reason ?? "", body.ExpectedVersion, Actor()), ct) });

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = Approve)]
    public async Task<IActionResult> ApproveRun(Guid id, [FromBody] VersionBody body, CancellationToken ct) =>
        Ok(new { version = await mediator.Send(new ApprovePayrollRunCommand(id, body.ExpectedVersion, Actor()), ct) });

    // ── paying out ───────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/lock")]
    [Authorize(Policy = Lock)]
    public async Task<IActionResult> LockRun(Guid id, CancellationToken ct)
    {
        await mediator.Send(new LockPayrollRunCommand(id, Actor()), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = Reopen)]
    public async Task<IActionResult> ReopenRun(Guid id, CancellationToken ct)
    {
        await mediator.Send(new ReopenPayrollRunCommand(id, Actor()), ct);
        return NoContent();
    }
}

public record StartRunBody(int Year, int Month);
public record VersionBody(long ExpectedVersion);
public record UpdateLineBody(decimal? Pcb, bool Excluded, string? ExcludedReason, long ExpectedVersion);
public record ReturnBody(string? Reason, long ExpectedVersion);
