using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Api.Filters;
using HRNexus.Application.StaffEntries.Commands;
using HRNexus.Application.StaffEntries.Queries;
using HRNexus.Domain.Constants;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/staff-entries")]
[Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LedgerManage)]
[FeatureRequired("staffLedger")]
public class StaffEntriesController(ISender sender) : ControllerBase
{
    // ── Entries ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries?staffId=&amp;year=&amp;month=</summary>
    [HttpGet]
    public async Task<IActionResult> ListEntries(
        [FromQuery] string staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var result = await sender.Send(new GetStaffEntriesQuery(staffGuid, year, month), ct);
        return Ok(result);
    }

    /// <summary>POST /api/staff-entries</summary>
    [HttpPost]
    public async Task<IActionResult> CreateEntry([FromBody] UpsertEntryBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        try
        {
            var id = await sender.Send(new CreateStaffEntryCommand(
                staffGuid, body.EntryDate, body.WorkType,
                body.VenueUnits, body.VenueRate, body.VenueEarnings,
                body.PrivateGross, body.PrivateNet,
                body.Settlement, body.Notes), ct);
            return CreatedAtAction(nameof(ListEntries), new { }, new { id = id.ToString() });
        }
        catch (KeyNotFoundException ex)  { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)     { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>PUT /api/staff-entries/{id}</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateEntry(string id, [FromBody] UpsertEntryBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();

        try
        {
            await sender.Send(new UpdateStaffEntryCommand(
                guid, body.EntryDate, body.WorkType,
                body.VenueUnits, body.VenueRate, body.VenueEarnings,
                body.PrivateGross, body.PrivateNet,
                body.Settlement, body.Notes), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)  { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)     { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>DELETE /api/staff-entries/{id}</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEntry(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();

        try
        {
            await sender.Send(new DeleteStaffEntryCommand(guid), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ── Expenses ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries/expenses?staffId=&amp;year=&amp;month=</summary>
    [HttpGet("expenses")]
    public async Task<IActionResult> ListExpenses(
        [FromQuery] string staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var result = await sender.Send(new GetStaffExpensesQuery(staffGuid, year, month), ct);
        return Ok(result);
    }

    /// <summary>POST /api/staff-entries/expenses</summary>
    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] UpsertExpenseBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        try
        {
            var id = await sender.Send(new CreateStaffExpenseCommand(
                staffGuid, body.Year, body.Month, body.Category, body.Amount, body.Notes), ct);
            return CreatedAtAction(nameof(ListExpenses), new { }, new { id = id.ToString() });
        }
        catch (KeyNotFoundException ex)  { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)     { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>PUT /api/staff-entries/expenses/{id}</summary>
    [HttpPut("expenses/{id}")]
    public async Task<IActionResult> UpdateExpense(string id, [FromBody] UpsertExpenseBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();

        try
        {
            await sender.Send(new UpdateStaffExpenseCommand(guid, body.Category, body.Amount, body.Notes), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>DELETE /api/staff-entries/expenses/{id}</summary>
    [HttpDelete("expenses/{id}")]
    public async Task<IActionResult> DeleteExpense(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();

        try
        {
            await sender.Send(new DeleteStaffExpenseCommand(guid), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ── Loans ─────────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries/loans?staffId=</summary>
    [HttpGet("loans")]
    public async Task<IActionResult> ListLoans([FromQuery] string staffId, CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var result = await sender.Send(new GetStaffLoansQuery(staffGuid), ct);
        return Ok(result);
    }

    /// <summary>POST /api/staff-entries/loans</summary>
    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] UpsertLoanBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        try
        {
            var id = await sender.Send(new CreateStaffLoanCommand(
                staffGuid, body.LoanDate, body.LoanType, body.Amount, body.Purpose), ct);
            return CreatedAtAction(nameof(ListLoans), new { }, new { id = id.ToString() });
        }
        catch (KeyNotFoundException ex)  { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex)     { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>DELETE /api/staff-entries/loans/{id}</summary>
    [HttpDelete("loans/{id}")]
    public async Task<IActionResult> DeleteLoan(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();

        try
        {
            await sender.Send(new DeleteStaffLoanCommand(guid), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries/summary?year=&amp;month= — monthly payout for all staff</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var result = await sender.Send(new GetLedgerSummaryQuery(year, month), ct);
        return Ok(result);
    }

    /// <summary>GET /api/staff-entries/outstanding — all PENDING / NOTCOLLECTED entries across all staff</summary>
    [HttpGet("outstanding")]
    public async Task<IActionResult> Outstanding(CancellationToken ct)
    {
        var result = await sender.Send(new GetOutstandingEntriesQuery(), ct);
        return Ok(result);
    }

    // ── Bulk settle ────────────────────────────────────────────────────────────

    /// <summary>PUT /api/staff-entries/bulk-settle — mark multiple entries as settled in one call</summary>
    [HttpPut("bulk-settle")]
    public async Task<IActionResult> BulkSettle([FromBody] BulkSettleBody body, CancellationToken ct)
    {
        var guids = body.EntryIds
            .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        if (guids.Count == 0)
            return BadRequest(new { error = "No valid entry IDs provided." });

        try
        {
            var updated = await sender.Send(new BulkSettleEntriesCommand(guids, body.Settlement), ct);
            return Ok(new { updated });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}

// ── Request / Response records ────────────────────────────────────────────────

public record UpsertEntryBody(
    string  StaffId,
    string  EntryDate,
    string  WorkType,
    decimal VenueUnits,
    decimal VenueRate,
    decimal VenueEarnings,
    decimal PrivateGross,
    decimal PrivateNet,
    string  Settlement,
    string? Notes);

public record UpsertExpenseBody(
    string  StaffId,
    int     Year,
    int     Month,
    string  Category,
    decimal Amount,
    string? Notes);

public record UpsertLoanBody(
    string  StaffId,
    string  LoanDate,
    string  LoanType,
    decimal Amount,
    string? Purpose);

public record BulkSettleBody(List<string> EntryIds, string Settlement);
