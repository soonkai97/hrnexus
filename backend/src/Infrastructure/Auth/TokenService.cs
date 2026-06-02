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
        // Short-lived access token: the long-lived (30-day) refresh cookie carries the "stay signed
        // in" session, and every refresh re-checks IsActive and re-issues fresh role/claims — so
        // deactivating or demoting a user takes effect within this window instead of after 30 days.
        var minutes = int.TryParse(config["Jwt:AccessTokenMinutes"], out var m) ? m : 30;
        var token = new JwtSecurityToken(
            issuer:   config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims:   claims,
            expires:  DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
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
