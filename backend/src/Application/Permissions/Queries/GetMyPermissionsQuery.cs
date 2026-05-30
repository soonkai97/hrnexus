using MediatR;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Permissions.Queries;

/// <summary>Effective permission keys for the currently-signed-in user.</summary>
public record GetMyPermissionsQuery : IRequest<List<string>>;

public class GetMyPermissionsHandler(IPermissionResolver resolver, ICurrentUserService currentUser)
    : IRequestHandler<GetMyPermissionsQuery, List<string>>
{
    public async Task<List<string>> Handle(GetMyPermissionsQuery req, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null) return [];
        var perms = await resolver.GetEffectivePermissionsAsync(userId.Value, ct);
        return perms.ToList();
    }
}
