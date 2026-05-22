using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Constants;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Api.Controllers;

[ApiController]
[Route("api/staff-entries")]
[Authorize(Roles = AppConstants.Roles.ManagerOrAdminClaim)]
public class StaffEntriesController(IAppDbContext db) : ControllerBase
{
    // ── Entries ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries?staffId=&year=&month=</summary>
    [HttpGet]
    public async Task<IActionResult> ListEntries(
        [FromQuery] string staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var entries = await db.StaffEntries
            .Where(e => e.StaffId == staffGuid && e.EntryDate.Year == year && e.EntryDate.Month == month)
            .OrderBy(e => e.EntryDate)
            .Select(e => new StaffEntryDto(
                e.Id.ToString(), e.StaffId.ToString(), e.EntryDate.ToString("yyyy-MM-dd"),
                e.WorkType, e.VenueUnits, e.VenueRate, e.VenueEarnings,
                e.PrivateGross, e.PrivateNet, e.Settlement, e.Notes,
                e.CreatedAt, e.UpdatedAt))
            .ToListAsync(ct);

        return Ok(entries);
    }

    /// <summary>POST /api/staff-entries</summary>
    [HttpPost]
    public async Task<IActionResult> CreateEntry([FromBody] UpsertEntryBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        if (!await db.Staff.AnyAsync(s => s.Id == staffGuid, ct))
            return NotFound(new { error = "Staff member not found." });

        if (!DateOnly.TryParse(body.EntryDate, out var date))
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

        var entry = StaffEntry.Create(
            staffGuid, date, body.WorkType,
            body.VenueUnits, body.VenueRate, body.VenueEarnings,
            body.PrivateGross, body.PrivateNet,
            body.Settlement, body.Notes);

        db.StaffEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListEntries), new { }, new { id = entry.Id.ToString() });
    }

    /// <summary>PUT /api/staff-entries/{id}</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateEntry(string id, [FromBody] UpsertEntryBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var entry = await db.StaffEntries.FindAsync([guid], ct);
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

    /// <summary>DELETE /api/staff-entries/{id}</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEntry(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var entry = await db.StaffEntries.FindAsync([guid], ct);
        if (entry is null) return NotFound();
        entry.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Expenses ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries/expenses?staffId=&year=&month=</summary>
    [HttpGet("expenses")]
    public async Task<IActionResult> ListExpenses(
        [FromQuery] string staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var expenses = await db.StaffExpenses
            .Where(e => e.StaffId == staffGuid && e.Year == year && e.Month == month)
            .OrderBy(e => e.Category)
            .Select(e => new StaffExpenseDto(e.Id.ToString(), e.StaffId.ToString(), e.Year, e.Month, e.Category, e.Amount, e.Notes))
            .ToListAsync(ct);

        return Ok(expenses);
    }

    /// <summary>POST /api/staff-entries/expenses</summary>
    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] UpsertExpenseBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        if (!await db.Staff.AnyAsync(s => s.Id == staffGuid, ct))
            return NotFound(new { error = "Staff member not found." });

        var expense = StaffExpense.Create(staffGuid, body.Year, body.Month, body.Category, body.Amount, body.Notes);
        db.StaffExpenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListExpenses), new { }, new { id = expense.Id.ToString() });
    }

    /// <summary>PUT /api/staff-entries/expenses/{id}</summary>
    [HttpPut("expenses/{id}")]
    public async Task<IActionResult> UpdateExpense(string id, [FromBody] UpsertExpenseBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var expense = await db.StaffExpenses.FindAsync([guid], ct);
        if (expense is null) return NotFound();
        expense.Update(body.Category, body.Amount, body.Notes);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>DELETE /api/staff-entries/expenses/{id}</summary>
    [HttpDelete("expenses/{id}")]
    public async Task<IActionResult> DeleteExpense(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var expense = await db.StaffExpenses.FindAsync([guid], ct);
        if (expense is null) return NotFound();
        expense.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Loans ─────────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries/loans?staffId=</summary>
    [HttpGet("loans")]
    public async Task<IActionResult> ListLoans([FromQuery] string staffId, CancellationToken ct)
    {
        if (!Guid.TryParse(staffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        var loans = await db.StaffLoans
            .Where(l => l.StaffId == staffGuid)
            .OrderBy(l => l.LoanDate)
            .Select(l => new StaffLoanDto(l.Id.ToString(), l.StaffId.ToString(), l.LoanDate.ToString("yyyy-MM-dd"), l.LoanType, l.Amount, l.Purpose, l.CreatedAt))
            .ToListAsync(ct);

        return Ok(loans);
    }

    /// <summary>POST /api/staff-entries/loans</summary>
    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] UpsertLoanBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.StaffId, out var staffGuid))
            return BadRequest(new { error = "Invalid staffId." });

        if (!await db.Staff.AnyAsync(s => s.Id == staffGuid, ct))
            return NotFound(new { error = "Staff member not found." });

        if (!DateOnly.TryParse(body.LoanDate, out var date))
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

        var loan = StaffLoan.Create(staffGuid, date, body.LoanType, body.Amount, body.Purpose);
        db.StaffLoans.Add(loan);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListLoans), new { }, new { id = loan.Id.ToString() });
    }

    /// <summary>DELETE /api/staff-entries/loans/{id}</summary>
    [HttpDelete("loans/{id}")]
    public async Task<IActionResult> DeleteLoan(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var loan = await db.StaffLoans.FindAsync([guid], ct);
        if (loan is null) return NotFound();
        loan.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-entries/summary?year=&amp;month= — monthly payout for all staff</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var grouped = await db.StaffEntries
            .Where(e => !e.IsDeleted && e.EntryDate.Year == year && e.EntryDate.Month == month)
            .GroupBy(e => e.StaffId)
            .Select(g => new
            {
                StaffId           = g.Key,
                VenueEarnings     = g.Sum(e => e.VenueEarnings),
                PrivateNet        = g.Sum(e => e.PrivateNet),
                EntryCount        = g.Count(),
                PendingCount      = g.Count(e => e.Settlement == "PENDING"),
                NotCollectedCount = g.Count(e => e.Settlement == "NOTCOLLECTED"),
            })
            .ToListAsync(ct);

        var staffIds  = grouped.Select(g => g.StaffId).ToList();
        var staffDict = await db.Staff
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id,
                s => new { s.StaffId, Name = (s.FirstName + " " + s.LastName).Trim() }, ct);

        var result = grouped
            .Select(g =>
            {
                staffDict.TryGetValue(g.StaffId, out var info);
                return new StaffLedgerPayoutDto(
                    g.StaffId.ToString(),
                    info?.StaffId ?? "",
                    info?.Name    ?? "Unknown",
                    g.VenueEarnings,
                    g.PrivateNet,
                    g.VenueEarnings + g.PrivateNet,
                    g.EntryCount,
                    g.PendingCount,
                    g.NotCollectedCount);
            })
            .OrderBy(r => r.StaffCode)
            .ToList();

        return Ok(result);
    }

    /// <summary>GET /api/staff-entries/outstanding — all PENDING / NOTCOLLECTED entries across all staff</summary>
    [HttpGet("outstanding")]
    public async Task<IActionResult> Outstanding(CancellationToken ct)
    {
        var entries = await db.StaffEntries
            .Where(e => !e.IsDeleted && (e.Settlement == "PENDING" || e.Settlement == "NOTCOLLECTED"))
            .OrderBy(e => e.EntryDate)
            .ToListAsync(ct);

        var staffIds  = entries.Select(e => e.StaffId).Distinct().ToList();
        var staffDict = await db.Staff
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id,
                s => new { s.StaffId, Name = (s.FirstName + " " + s.LastName).Trim() }, ct);

        var result = entries
            .Select(e =>
            {
                staffDict.TryGetValue(e.StaffId, out var info);
                return new StaffOutstandingDto(
                    e.Id.ToString(),
                    e.StaffId.ToString(),
                    info?.StaffId ?? "",
                    info?.Name    ?? "Unknown",
                    e.EntryDate.ToString("yyyy-MM-dd"),
                    e.WorkType,
                    e.VenueEarnings,
                    e.PrivateNet,
                    e.Settlement);
            })
            .ToList();

        return Ok(result);
    }

    // ── Bulk settle ────────────────────────────────────────────────────────────

    /// <summary>PUT /api/staff-entries/bulk-settle — mark multiple entries as settled in one call</summary>
    [HttpPut("bulk-settle")]
    public async Task<IActionResult> BulkSettle([FromBody] BulkSettleBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Settlement))
            return BadRequest(new { error = "Settlement is required." });

        var guids = body.EntryIds
            .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        if (guids.Count == 0)
            return BadRequest(new { error = "No valid entry IDs provided." });

        var entries = await db.StaffEntries
            .Where(e => guids.Contains(e.Id) && !e.IsDeleted)
            .ToListAsync(ct);

        foreach (var entry in entries)
            entry.Settle(body.Settlement);

        await db.SaveChangesAsync(ct);
        return Ok(new { updated = entries.Count });
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

public record StaffEntryDto(
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

public record StaffExpenseDto(
    string  Id,
    string  StaffId,
    int     Year,
    int     Month,
    string  Category,
    decimal Amount,
    string? Notes);

public record StaffLoanDto(
    string   Id,
    string   StaffId,
    string   LoanDate,
    string   LoanType,
    decimal  Amount,
    string?  Purpose,
    DateTime CreatedAt);

public record StaffSummaryDto(
    string  StaffId,
    decimal VenueEarnings,
    decimal PrivateGross,
    decimal PrivateNet,
    int     EntryCount);

public record StaffLedgerPayoutDto(
    string  StaffId,
    string  StaffCode,
    string  StaffName,
    decimal VenueEarnings,
    decimal PrivateNet,
    decimal NetPayout,
    int     EntryCount,
    int     PendingCount,
    int     NotCollectedCount);

public record StaffOutstandingDto(
    string  EntryId,
    string  StaffId,
    string  StaffCode,
    string  StaffName,
    string  EntryDate,
    string  WorkType,
    decimal VenueEarnings,
    decimal PrivateNet,
    string  Settlement);
