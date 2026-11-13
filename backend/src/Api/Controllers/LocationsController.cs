using System.Security.Claims;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Locations.Commands;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationsController(IAppDbContext db, IFileStorage files, IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name, l.NameZh, l.NameVi, l.ImagePath))
            .ToListAsync(ct);
        return Ok(list);
    }

    /// <summary>The locations the signed-in staff member is assigned to (the only ones they may log shifts at).</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var callerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var assignedIds = await db.StaffLocations
            .Where(sl => sl.EmployeeId == callerId)
            .Select(sl => sl.LocationId)
            .ToListAsync(ct);
        var list = await db.Locations
            .Where(l => assignedIds.Contains(l.Id))
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name, l.NameZh, l.NameVi, l.ImagePath))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LocationsManage)]
    public async Task<IActionResult> Create([FromBody] UpsertLocationBody body, CancellationToken ct)
    {
        var name = body.Name.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Location name is required." });
        if (await db.Locations.AnyAsync(l => l.Name == name, ct))
            return Conflict(new { error = $"Location '{name}' already exists." });
        var loc = Location.Create(name, body.NameZh, body.NameVi);
        db.Locations.Add(loc);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { }, new { loc.Id });
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LocationsManage)]
    public async Task<IActionResult> Update(string id, [FromBody] UpsertLocationBody body, CancellationToken ct)
    {
        var loc = await db.Locations.FindAsync([id], ct);
        if (loc is null) return NotFound();
        var name = body.Name.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Location name is required." });
        if (await db.Locations.AnyAsync(l => l.Name == name && l.Id != id, ct))
            return Conflict(new { error = $"Location '{name}' already exists." });
        loc.Update(name, body.NameZh, body.NameVi);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id}/image")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LocationsManage)]
    public async Task<IActionResult> UploadImage(string id, IFormFile file, CancellationToken ct)
    {
        var loc = await db.Locations.FindAsync([id], ct);
        if (loc is null) return NotFound();
        if (file.Length == 0) return BadRequest(new { error = "Empty file." });
        if (file.Length > AppConstants.Limits.ImageMaxBytes) return BadRequest(new { error = "Image must be smaller than 5 MB." });

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext)) return BadRequest(new { error = "Only JPG, PNG or WebP allowed." });

        if (loc.ImagePath is not null) files.Delete(loc.ImagePath);

        await using var stream = file.OpenReadStream();
        var path = await files.SaveAsync(stream, file.FileName, "locations", ct);
        loc.SetImage(path);
        await db.SaveChangesAsync(ct);
        return Ok(new { path });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = AppConstants.Permissions.PolicyPrefix + AppConstants.Permissions.LocationsManage)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await mediator.Send(new DeleteLocationCommand(id), ct);
        return NoContent();
    }
}

public record LocationDto(string Id, string Name, string? NameZh, string? NameVi, string? ImagePath);
public record UpsertLocationBody(string Name, string? NameZh = null, string? NameVi = null);
