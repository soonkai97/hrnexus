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
        var roleId = await db.Roles.Where(r => r.Name == name).Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (roleId == Guid.Empty)
            throw new KeyNotFoundException($"Role '{req.RoleName}' not found.");

        return await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(ct);
    }
}
