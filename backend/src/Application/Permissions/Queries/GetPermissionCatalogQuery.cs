using HRNexus.Domain.Constants;
using MediatR;

namespace HRNexus.Application.Permissions.Queries;

/// <summary>The full catalog of assignable permission keys (code-defined).</summary>
public record GetPermissionCatalogQuery : IRequest<List<string>>;

public class GetPermissionCatalogHandler : IRequestHandler<GetPermissionCatalogQuery, List<string>>
{
    public Task<List<string>> Handle(GetPermissionCatalogQuery req, CancellationToken ct)
        => Task.FromResult(AppConstants.Permissions.All.ToList());
}
