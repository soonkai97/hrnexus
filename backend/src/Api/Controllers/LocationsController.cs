using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Constants;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Api.Controllers;

[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationsController(IAppDbContext db, IFileStorage files) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name, l.ImagePath))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Create([FromBody] UpsertLocationBody body, CancellationToken ct)
    {
        var name = body.Name.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Location name is required." });
        if (await db.Locations.AnyAsync(l => l.Name == name, ct))
            return Conflict(new { error = $"Location '{name}' already exists." });
        var loc = Location.Create(name);
        db.Locations.Add(loc);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { }, new { loc.Id });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Update(string id, [FromBody] UpsertLocationBody body, CancellationToken ct)
    {
        var loc = await db.Locations.FindAsync([id], ct);
        if (loc is null) return NotFound();
        var name = body.Name.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Location name is required." });
        if (await db.Locations.AnyAsync(l => l.Name == name && l.Id != id, ct))
            return Conflict(new { error = $"Location '{name}' already exists." });
        loc.Update(name);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id}/image")]
    [Authorize(Roles = "Manager,Admin")]
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
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var loc = await db.Locations.FindAsync([id], ct);
        if (loc is null) return NotFound();
        loc.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record LocationDto(string Id, string Name, string? ImagePath);
public record UpsertLocationBody(string Name);
