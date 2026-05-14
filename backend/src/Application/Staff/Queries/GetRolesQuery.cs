using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Staff.Queries;

public record GetRolesQuery : IRequest<List<RoleDto>>;

public record RoleDto(string Name, bool IsSystem, decimal HourlyRate);

public class GetRolesQueryHandler(IAppDbContext db)
    : IRequestHandler<GetRolesQuery, List<RoleDto>>
{
    public async Task<List<RoleDto>> Handle(GetRolesQuery req, CancellationToken ct) =>
        await db.Roles
            .OrderBy(r => r.IsSystem ? 1 : 0).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Name, r.IsSystem, r.HourlyRate))
            .ToListAsync(ct);
}
