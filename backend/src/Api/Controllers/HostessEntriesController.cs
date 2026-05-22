using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Api.Controllers;

[ApiController]
[Route("api/hostess-entries")]
[Authorize(Roles = "manager,admin")]
public class HostessEntriesController(IAppDbContext db) : ControllerBase
{
    // ── Entries ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/hostess-entries?staffId=&year=&month=</summary>
    [HttpGet]
    public async Task<IActionResult> ListEntries(
        [FromQuery] string staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var entries = await db.HostessEntries
            .Where(e => e.StaffId == staffGuid && e.EntryDate.Year == year && e.EntryDate.Month == month)
            .OrderBy(e => e.EntryDate)
            .Select(e => new HostessEntryDto(
                e.Id.ToString(), e.StaffId.ToString(), e.EntryDate.ToString("yyyy-MM-dd"),
                e.WorkType, e.VenueUnits, e.VenueRate, e.VenueEarnings,
                e.PrivateGross, e.PrivateNet, e.Settlement, e.Notes,
                e.CreatedAt, e.UpdatedAt))
            .ToListAsync(ct);

        return Ok(entries);
    }

    /// <summary>POST /api/hostess-entries</summary>
    [HttpPost]
    public async Task<IActionResult> CreateEntry([FromBody] UpsertEntryBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        if (!await db.Staff.AnyAsync(s => s.Id == staffGuid, ct))
            return NotFound(new { error = "Staff member not found." });

        if (!DateOnly.TryParse(body.EntryDate, out var date))
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

        var entry = HostessEntry.Create(
            staffGuid, date, body.WorkType,
            body.VenueUnits, body.VenueRate, body.VenueEarnings,
            body.PrivateGross, body.PrivateNet,
            body.Settlement, body.Notes);

        db.HostessEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListEntries), new { }, new { id = entry.Id.ToString() });
    }

    /// <summary>PUT /api/hostess-entries/{id}</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateEntry(string id, [FromBody] UpsertEntryBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var entry = await db.HostessEntries.FindAsync([guid], ct);
        if (entry is null) return NotFound();

        if (!DateOnly.TryParse(body.EntryDate, out var date))
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

        entry.Update(date, body.WorkType,
            body.VenueUnits, body.VenueRate, body.VenueEarnings,
            body.PrivateGross, body.PrivateNet,
            body.Settlement, body.Notes);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>DELETE /api/hostess-entries/{id}</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEntry(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var entry = await db.HostessEntries.FindAsync([guid], ct);
        if (entry is null) return NotFound();
        entry.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Expenses ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/hostess-entries/expenses?staffId=&year=&month=</summary>
    [HttpGet("expenses")]
    public async Task<IActionResult> ListExpenses(
        [FromQuery] string staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var expenses = await db.HostessExpenses
            .Where(e => e.StaffId == staffGuid && e.Year == year && e.Month == month)
            .OrderBy(e => e.Category)
            .Select(e => new HostessExpenseDto(e.Id.ToString(), e.StaffId.ToString(), e.Year, e.Month, e.Category, e.Amount, e.Notes))
            .ToListAsync(ct);

        return Ok(expenses);
    }

    /// <summary>POST /api/hostess-entries/expenses</summary>
    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] UpsertExpenseBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        if (!await db.Staff.AnyAsync(s => s.Id == staffGuid, ct))
            return NotFound(new { error = "Staff member not found." });

        var expense = HostessExpense.Create(staffGuid, body.Year, body.Month, body.Category, body.Amount, body.Notes);
        db.HostessExpenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListExpenses), new { }, new { id = expense.Id.ToString() });
    }

    /// <summary>PUT /api/hostess-entries/expenses/{id}</summary>
    [HttpPut("expenses/{id}")]
    public async Task<IActionResult> UpdateExpense(string id, [FromBody] UpsertExpenseBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var expense = await db.HostessExpenses.FindAsync([guid], ct);
        if (expense is null) return NotFound();
        expense.Update(body.Category, body.Amount, body.Notes);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>DELETE /api/hostess-entries/expenses/{id}</summary>
    [HttpDelete("expenses/{id}")]
    public async Task<IActionResult> DeleteExpense(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var expense = await db.HostessExpenses.FindAsync([guid], ct);
        if (expense is null) return NotFound();
        expense.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Loans ─────────────────────────────────────────────────────────────────

    /// <summary>GET /api/hostess-entries/loans?staffId=</summary>
    [HttpGet("loans")]
    public async Task<IActionResult> ListLoans([FromQuery] string staffId, CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var loans = await db.HostessLoans
            .Where(l => l.StaffId == staffGuid)
            .OrderBy(l => l.LoanDate)
            .Select(l => new HostessLoanDto(l.Id.ToString(), l.StaffId.ToString(), l.LoanDate.ToString("yyyy-MM-dd"), l.LoanType, l.Amount, l.Purpose, l.CreatedAt))
            .ToListAsync(ct);

        return Ok(loans);
    }

    /// <summary>POST /api/hostess-entries/loans</summary>
    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] UpsertLoanBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        if (!await db.Staff.AnyAsync(s => s.Id == staffGuid, ct))
            return NotFound(new { error = "Staff member not found." });

        if (!DateOnly.TryParse(body.LoanDate, out var date))
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

        var loan = HostessLoan.Create(staffGuid, date, body.LoanType, body.Amount, body.Purpose);
        db.HostessLoans.Add(loan);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListLoans), new { }, new { id = loan.Id.ToString() });
    }

    /// <summary>DELETE /api/hostess-entries/loans/{id}</summary>
    [HttpDelete("loans/{id}")]
    public async Task<IActionResult> DeleteLoan(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var loan = await db.HostessLoans.FindAsync([guid], ct);
        if (loan is null) return NotFound();
        loan.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/hostess-entries/summary?year=&month= — totals for all hostesses</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var entries = await db.HostessEntries
            .Where(e => e.EntryDate.Year == year && e.EntryDate.Month == month)
            .GroupBy(e => e.StaffId)
            .Select(g => new HostessSummaryDto(
                g.Key.ToString(),
                g.Sum(e => e.VenueEarnings),
                g.Sum(e => e.PrivateGross),
                g.Sum(e => e.PrivateNet),
                g.Count()))
            .ToListAsync(ct);

        return Ok(entries);
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

public record HostessEntryDto(
    string   Id,
    string   StaffId,
    string   EntryDate,
    string   WorkType,
    decimal  VenueUnits,
    decimal  VenueRate,
    decimal  VenueEarnings,
    decimal  PrivateGross,
    decimal  PrivateNet,
    string   Settlement,
    string?  Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record HostessExpenseDto(
    string  Id,
    string  StaffId,
    int     Year,
    int     Month,
    string  Category,
    decimal Amount,
    string? Notes);

public record HostessLoanDto(
    string   Id,
    string   StaffId,
    string   LoanDate,
    string   LoanType,
    decimal  Amount,
    string?  Purpose,
    DateTime CreatedAt);

public record HostessSummaryDto(
    string  StaffId,
    decimal VenueEarnings,
    decimal PrivateGross,
    decimal PrivateNet,
    int     EntryCount);
