using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Queries;

public record GetRolesQuery : IRequest<List<RoleDto>>;

public record RoleDto(string Name, bool IsSystem, decimal HourlyRate, string? NameZh, string? NameVi, string PayType);

public class GetRolesQueryHandler(IAppDbContext db)
    : IRequestHandler<GetRolesQuery, List<RoleDto>>
{
    public async Task<List<RoleDto>> Handle(GetRolesQuery req, CancellationToken ct) =>
        await db.Roles
            .OrderBy(r => r.IsSystem ? 1 : 0).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Name, r.IsSystem, r.HourlyRate, r.NameZh, r.NameVi, r.PayType))
            .ToListAsync(ct);
}
