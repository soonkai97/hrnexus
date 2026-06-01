using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Locations.Commands;

/// <summary>Result of a bulk-create: how many rows were newly created vs skipped as duplicates.</summary>
public record BulkCreateResult(int Created, int Skipped);

/// <summary>Create many venue locations at once from a list of names. Names are normalised
/// (trim + UPPER), de-duplicated within the batch, and any that already exist are skipped.</summary>
public record BulkCreateLocationsCommand(List<string> Names) : IRequest<BulkCreateResult>;

public class BulkCreateLocationsCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<BulkCreateLocationsCommand, BulkCreateResult>
{
    public async Task<BulkCreateResult> Handle(BulkCreateLocationsCommand req, CancellationToken ct)
    {
        var names = req.Names.Select(n => n.Trim().ToUpperInvariant())
            .Where(n => n.Length > 0).Distinct().ToList();
        if (names.Count == 0) return new BulkCreateResult(0, 0);
        if (names.Count > 100)
            throw new ArgumentException("Cannot create more than 100 locations at once.");

        var existing = await db.Locations.Where(l => names.Contains(l.Name))
            .Select(l => l.Name).ToListAsync(ct);
        var toCreate = names.Where(n => !existing.Contains(n)).ToList();

        foreach (var name in toCreate)
        {
            var loc = E.Location.Create(name);
            db.Locations.Add(loc);
            await audit.LogAsync("Location", loc.Id, "BulkCreate", null, "Admin", name, ct);
        }
        await db.SaveChangesAsync(ct);
        return new BulkCreateResult(toCreate.Count, names.Count - toCreate.Count);
    }
}
