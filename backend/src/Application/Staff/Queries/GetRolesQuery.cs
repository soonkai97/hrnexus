using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Staff.Queries;

public record GetRolesQuery : IRequest<List<RoleDto>>;

public record RoleDto(Guid Id, string Name, bool IsSystem);

public class GetRolesQueryHandler(IAppDbContext db)
    : IRequestHandler<GetRolesQuery, List<RoleDto>>
{
    public async Task<List<RoleDto>> Handle(GetRolesQuery req, CancellationToken ct) =>
        await db.Roles
            .OrderBy(r => r.IsSystem ? 1 : 0).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.IsSystem))
            .ToListAsync(ct);
}
