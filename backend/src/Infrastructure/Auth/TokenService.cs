using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Auth;

public class TokenService(IConfiguration config) : ITokenService
{
    public string GenerateAccessToken(Staff employee)
    {
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   employee.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, employee.Email ?? ""),
            new Claim(ClaimTypes.Role,               employee.PermissionLevel),
            new Claim("fullName",                    employee.FullName),
            new Claim("staffId",                     employee.StaffId),
        };
        var days  = int.TryParse(config["Jwt:AccessTokenDays"], out var d) ? d : 30;
        var token = new JwtSecurityToken(
            issuer:   config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims:   claims,
            expires:  DateTime.UtcNow.AddDays(days),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    // The 2FA challenge token uses a distinct audience so AddJwtBearer (which validates the access
    // audience) refuses it for [Authorize] endpoints — it's only honoured at /api/auth/2fa/verify.
    private string TwoFactorAudience => $"{config["Jwt:Audience"]}.2fa";

    public string GenerateTwoFactorToken(Guid staffId)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:   config["Jwt:Issuer"],
            audience: TwoFactorAudience,
            claims:   new[] { new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString()) },
            expires:  DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Guid? ValidateTwoFactorToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        try
        {
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = key,
                ValidateIssuer          = true,
                ValidIssuer             = config["Jwt:Issuer"],
                ValidateAudience        = true,
                ValidAudience           = TwoFactorAudience,
                ClockSkew               = TimeSpan.Zero,
            }, out var validated);
            return Guid.Parse(((JwtSecurityToken)validated).Subject);
        }
        catch
        {
            return null;
        }
    }

    public Guid? ValidateAccessToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        try
        {
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = key,
                ValidateIssuer          = true,
                ValidIssuer             = config["Jwt:Issuer"],
                ValidateAudience        = true,
                ValidAudience           = config["Jwt:Audience"],
                ClockSkew               = TimeSpan.Zero,
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            return Guid.Parse(jwt.Subject);
        }
        catch
        {
            return null;
        }
    }
}
