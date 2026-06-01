using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Locations.Commands;   // BulkCreateResult
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Staff.Commands;

public record BulkRoleInput(string Name, decimal HourlyRate);

/// <summary>Create many custom roles at once. Names are normalised (trim + UPPER), de-duplicated
/// within the batch, and any already-existing role is skipped. Each new role seeds a rate-history
/// baseline so shifts are priced as-of their work date (mirrors CreateRoleCommand).</summary>
public record BulkCreateRolesCommand(List<BulkRoleInput> Roles) : IRequest<BulkCreateResult>;

public class BulkCreateRolesCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<BulkCreateRolesCommand, BulkCreateResult>
{
    public async Task<BulkCreateResult> Handle(BulkCreateRolesCommand req, CancellationToken ct)
    {
        var rows = req.Roles
            .Select(r => new BulkRoleInput(r.Name.Trim().ToUpperInvariant(), r.HourlyRate < 0 ? 0 : r.HourlyRate))
            .Where(r => r.Name.Length > 0)
            .GroupBy(r => r.Name).Select(g => g.First())   // de-dupe by name, keep first rate
            .ToList();
        if (rows.Count == 0) return new BulkCreateResult(0, 0);
        if (rows.Count > 100)
            throw new ArgumentException("Cannot create more than 100 roles at once.");

        var names    = rows.Select(r => r.Name).ToList();
        var existing = await db.Roles.Where(r => names.Contains(r.Name)).Select(r => r.Name).ToListAsync(ct);
        var toCreate = rows.Where(r => !existing.Contains(r.Name)).ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var r in toCreate)
        {
            db.Roles.Add(E.Role.Create(r.Name, hourlyRate: r.HourlyRate));
            db.RoleRates.Add(E.RoleRate.Create(r.Name, r.HourlyRate, today));
            await audit.LogAsync("Role", r.Name, "BulkCreate", null, "Admin", $"rate={r.HourlyRate}", ct);
        }
        await db.SaveChangesAsync(ct);
        return new BulkCreateResult(toCreate.Count, rows.Count - toCreate.Count);
    }
}
