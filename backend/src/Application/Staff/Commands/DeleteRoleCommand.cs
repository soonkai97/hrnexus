using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Staff.Commands;

public record DeleteRoleCommand(string Name) : IRequest;

public class DeleteRoleCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand req, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == req.Name.Trim().ToUpperInvariant(), ct)
            ?? throw new KeyNotFoundException($"Role '{req.Name}' not found.");

        if (role.IsSystem)
            throw new InvalidOperationException($"System role '{req.Name}' cannot be deleted.");

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }
}
