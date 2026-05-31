namespace HRNexus.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(HRNexus.Domain.Entities.Staff employee);
    string GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);

    /// <summary>Short-lived token issued after a correct password when 2FA is required. It carries a
    /// distinct audience so the normal bearer pipeline will NOT accept it as an access token — it can
    /// only be exchanged at the 2FA-verify endpoint.</summary>
    string GenerateTwoFactorToken(Guid staffId);

    /// <summary>Validates a 2FA challenge token and returns the staff id, or null if invalid/expired.</summary>
    Guid? ValidateTwoFactorToken(string token);
}
