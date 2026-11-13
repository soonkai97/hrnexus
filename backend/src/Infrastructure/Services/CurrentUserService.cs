using System.Security.Claims;
using HRNexus.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace HRNexus.Infrastructure.Services;

public class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var raw = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? UserName =>
        accessor.HttpContext?.User.FindFirstValue("staffId")
        ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
}
