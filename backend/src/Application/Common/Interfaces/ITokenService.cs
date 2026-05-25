using HRNexus.Domain.Entities;

namespace HRNexus.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(HRNexus.Domain.Entities.Staff employee);
    string GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
