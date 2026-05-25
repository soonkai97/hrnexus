using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/staff-config")]
[Authorize(Roles = AppConstants.Roles.ManagerOrAdminClaim)]
public class StaffConfigController(IAppDbContext db) : ControllerBase
{
    // ── Work Types ────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-config/work-types</summary>
    [HttpGet("work-types")]
    public async Task<IActionResult> ListWorkTypes(CancellationToken ct)
    {
        var items = await db.StaffWorkTypes
            .OrderBy(w => w.DisplayOrder)
            .Select(w => new StaffWorkTypeDto(w.Id.ToString(), w.Key, w.Label, w.AutoCalc, w.HasPrivate, w.DisplayOrder, w.IsActive))
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>POST /api/staff-config/work-types</summary>
    [HttpPost("work-types")]
    public async Task<IActionResult> CreateWorkType([FromBody] UpsertWorkTypeBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Key) || string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Key and Label are required." });

        var key = body.Key.Trim().ToUpperInvariant();
        if (await db.StaffWorkTypes.AnyAsync(w => w.Key == key, ct))
            return Conflict(new { error = $"Work type '{key}' already exists." });

        var item = StaffWorkType.Create(key, body.Label, body.AutoCalc, body.HasPrivate, body.DisplayOrder);
        db.StaffWorkTypes.Add(item);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListWorkTypes), new { }, new { id = item.Id.ToString() });
    }

    /// <summary>PUT /api/staff-config/work-types/{id}</summary>
    [HttpPut("work-types/{id}")]
    public async Task<IActionResult> UpdateWorkType(string id, [FromBody] UpsertWorkTypeBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffWorkTypes.FindAsync([guid], ct);
        if (item is null) return NotFound();

        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Label is required." });

        item.Update(body.Label, body.AutoCalc, body.HasPrivate, body.DisplayOrder, body.IsActive);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>DELETE /api/staff-config/work-types/{id}</summary>
    [HttpDelete("work-types/{id}")]
    public async Task<IActionResult> DeleteWorkType(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffWorkTypes.FindAsync([guid], ct);
        if (item is null) return NotFound();
        db.StaffWorkTypes.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Settlements ───────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-config/settlements</summary>
    [HttpGet("settlements")]
    public async Task<IActionResult> ListSettlements(CancellationToken ct)
    {
        var items = await db.StaffSettlements
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new StaffSettlementDto(s.Id.ToString(), s.Key, s.Label, s.Severity, s.DisplayOrder, s.IsActive))
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>POST /api/staff-config/settlements</summary>
    [HttpPost("settlements")]
    public async Task<IActionResult> CreateSettlement([FromBody] UpsertSettlementBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Key) || string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Key and Label are required." });

        var key = body.Key.Trim().ToUpperInvariant();
        if (await db.StaffSettlements.AnyAsync(s => s.Key == key, ct))
            return Conflict(new { error = $"Settlement '{key}' already exists." });

        var item = StaffSettlement.Create(key, body.Label, body.Severity, body.DisplayOrder);
        db.StaffSettlements.Add(item);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListSettlements), new { }, new { id = item.Id.ToString() });
    }

    /// <summary>PUT /api/staff-config/settlements/{id}</summary>
    [HttpPut("settlements/{id}")]
    public async Task<IActionResult> UpdateSettlement(string id, [FromBody] UpsertSettlementBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffSettlements.FindAsync([guid], ct);
        if (item is null) return NotFound();

        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Label is required." });

        item.Update(body.Label, body.Severity, body.DisplayOrder, body.IsActive);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>DELETE /api/staff-config/settlements/{id}</summary>
    [HttpDelete("settlements/{id}")]
    public async Task<IActionResult> DeleteSettlement(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffSettlements.FindAsync([guid], ct);
        if (item is null) return NotFound();
        db.StaffSettlements.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Expense Categories ────────────────────────────────────────────────────

    /// <summary>GET /api/staff-config/expense-categories</summary>
    [HttpGet("expense-categories")]
    public async Task<IActionResult> ListExpenseCategories(CancellationToken ct)
    {
        var items = await db.StaffExpenseCategories
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new StaffExpenseCategoryDto(c.Id.ToString(), c.Key, c.Label, c.DisplayOrder, c.IsActive))
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>POST /api/staff-config/expense-categories</summary>
    [HttpPost("expense-categories")]
    public async Task<IActionResult> CreateExpenseCategory([FromBody] UpsertExpenseCategoryBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Key) || string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Key and Label are required." });

        var key = body.Key.Trim().ToUpperInvariant();
        if (await db.StaffExpenseCategories.AnyAsync(c => c.Key == key, ct))
            return Conflict(new { error = $"Expense category '{key}' already exists." });

        var item = StaffExpenseCategory.Create(key, body.Label, body.DisplayOrder);
        db.StaffExpenseCategories.Add(item);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListExpenseCategories), new { }, new { id = item.Id.ToString() });
    }

    /// <summary>PUT /api/staff-config/expense-categories/{id}</summary>
    [HttpPut("expense-categories/{id}")]
    public async Task<IActionResult> UpdateExpenseCategory(string id, [FromBody] UpsertExpenseCategoryBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffExpenseCategories.FindAsync([guid], ct);
        if (item is null) return NotFound();

        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Label is required." });

        item.Update(body.Label, body.DisplayOrder, body.IsActive);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>DELETE /api/staff-config/expense-categories/{id}</summary>
    [HttpDelete("expense-categories/{id}")]
    public async Task<IActionResult> DeleteExpenseCategory(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffExpenseCategories.FindAsync([guid], ct);
        if (item is null) return NotFound();
        db.StaffExpenseCategories.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Loan Types ────────────────────────────────────────────────────────────

    /// <summary>GET /api/staff-config/loan-types</summary>
    [HttpGet("loan-types")]
    public async Task<IActionResult> ListLoanTypes(CancellationToken ct)
    {
        var items = await db.StaffLoanTypes
            .OrderBy(lt => lt.DisplayOrder).ThenBy(lt => lt.Key)
            .Select(lt => new StaffLoanTypeDto(
                lt.Id.ToString(), lt.Key, lt.Label,
                lt.IsAdvance, lt.DisplayOrder, lt.IsActive, lt.IsSystem))
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>POST /api/staff-config/loan-types</summary>
    [HttpPost("loan-types")]
    public async Task<IActionResult> CreateLoanType([FromBody] UpsertLoanTypeBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Key) || string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Key and Label are required." });

        var key = body.Key.Trim().ToUpperInvariant();
        if (await db.StaffLoanTypes.AnyAsync(lt => lt.Key == key, ct))
            return Conflict(new { error = $"Loan type '{key}' already exists." });

        var item = StaffLoanType.Create(key, body.Label, body.IsAdvance, body.DisplayOrder);
        db.StaffLoanTypes.Add(item);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListLoanTypes), new { }, new { id = item.Id.ToString() });
    }

    /// <summary>PUT /api/staff-config/loan-types/{id}</summary>
    [HttpPut("loan-types/{id}")]
    public async Task<IActionResult> UpdateLoanType(string id, [FromBody] UpsertLoanTypeBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffLoanTypes.FindAsync([guid], ct);
        if (item is null) return NotFound();

        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Label is required." });

        item.Update(body.Label, body.DisplayOrder, body.IsActive);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>DELETE /api/staff-config/loan-types/{id}</summary>
    [HttpDelete("loan-types/{id}")]
    public async Task<IActionResult> DeleteLoanType(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return BadRequest();
        var item = await db.StaffLoanTypes.FindAsync([guid], ct);
        if (item is null) return NotFound();
        if (item.IsSystem)
            return Conflict(new { error = "System loan types cannot be deleted." });
        db.StaffLoanTypes.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

// ── Request / Response records ────────────────────────────────────────────────

public record UpsertWorkTypeBody(
    string Key,
    string Label,
    bool   AutoCalc,
    bool   HasPrivate,
    int    DisplayOrder,
    bool   IsActive = true);

public record UpsertSettlementBody(
    string Key,
    string Label,
    string Severity,
    int    DisplayOrder,
    bool   IsActive = true);

public record StaffWorkTypeDto(
    string Id,
    string Key,
    string Label,
    bool   AutoCalc,
    bool   HasPrivate,
    int    DisplayOrder,
    bool   IsActive);

public record StaffSettlementDto(
    string Id,
    string Key,
    string Label,
    string Severity,
    int    DisplayOrder,
    bool   IsActive);

public record UpsertExpenseCategoryBody(
    string Key,
    string Label,
    int    DisplayOrder,
    bool   IsActive = true);

public record StaffExpenseCategoryDto(
    string Id,
    string Key,
    string Label,
    int    DisplayOrder,
    bool   IsActive);

public record UpsertLoanTypeBody(
    string Key,
    string Label,
    bool   IsAdvance,
    int    DisplayOrder,
    bool   IsActive = true);

public record StaffLoanTypeDto(
    string Id,
    string Key,
    string Label,
    bool   IsAdvance,
    int    DisplayOrder,
    bool   IsActive,
    bool   IsSystem);
