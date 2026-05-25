using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Staff.Commands;

public record CreateRoleCommand(string Name, decimal HourlyRate = 0) : IRequest;

public class CreateRoleCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateRoleCommand>
{
    public async Task Handle(CreateRoleCommand req, CancellationToken ct)
    {
        var name = req.Name.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Role name cannot be empty.");

        var exists = await db.Roles.AnyAsync(r => r.Name == name, ct);
        if (exists) throw new InvalidOperationException($"Role '{name}' already exists.");

        db.Roles.Add(Role.Create(name, hourlyRate: req.HourlyRate));
        await db.SaveChangesAsync(ct);
    }
}
