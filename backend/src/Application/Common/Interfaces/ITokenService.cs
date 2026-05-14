using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(NightclubWorkforce.Domain.Entities.Staff employee);
    string GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
