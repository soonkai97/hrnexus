using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Permissions.Queries;

/// <summary>Default permission keys granted by a role.</summary>
public record GetRolePermissionsQuery(string RoleName) : IRequest<List<string>>;

public class GetRolePermissionsHandler(IAppDbContext db)
    : IRequestHandler<GetRolePermissionsQuery, List<string>>
{
    public async Task<List<string>> Handle(GetRolePermissionsQuery req, CancellationToken ct)
    {
        var name = req.RoleName.Trim().ToUpperInvariant();
        if (!await db.Roles.AnyAsync(r => r.Name == name, ct))
            throw new KeyNotFoundException($"Role '{req.RoleName}' not found.");

        return await db.RolePermissions
            .Where(rp => rp.RoleName == name)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(ct);
    }
}
