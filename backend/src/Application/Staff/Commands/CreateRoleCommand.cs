using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Staff.Commands;

public record CreateRoleCommand(string Name, decimal HourlyRate = 0, string PayType = AppConstants.PayType.Hourly) : IRequest;

public class CreateRoleCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateRoleCommand>
{
    public async Task Handle(CreateRoleCommand req, CancellationToken ct)
    {
        var name = req.Name.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Role name cannot be empty.");

        var exists = await db.Roles.AnyAsync(r => r.Name == name, ct);
        if (exists) throw new InvalidOperationException($"Role '{name}' already exists.");

        var role = Role.Create(name, hourlyRate: req.HourlyRate, payType: req.PayType);
        db.Roles.Add(role);
        // Seed the rate-history baseline so shifts can be priced as-of their work date.
        db.RoleRates.Add(RoleRate.Create(role.Id, req.HourlyRate, DateOnly.FromDateTime(DateTime.UtcNow)));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // PK collision: concurrent request inserted the same role name between our
            // AnyAsync check and this save. Return the friendly duplicate error.
            throw new InvalidOperationException($"Role '{name}' already exists.");
        }
    }
}
