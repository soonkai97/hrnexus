using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Infrastructure.Auth;

public class FeatureMgmtService(IConfiguration config, IPasswordHasher hasher) : IFeatureMgmtService
{
    private const int MaxAttempts     = 5;
    private const int LockoutMinutes  = 15;
    private const int TokenHours      = 4;
    private const string Issuer       = "nc-feature-mgmt";
    private const string Audience     = "nc-feature-mgmt";

    // In-memory per-IP rate limit — resets on restart (acceptable for this use case)
    private static readonly ConcurrentDictionary<string, (int Attempts, DateTime LockedUntil)>
        _rate = new();

    public bool IsPinConfigured => !string.IsNullOrWhiteSpace(config["FeatureMgmt:PinHash"]);

    public string HashPin(string pin) => hasher.Hash(pin);

    public UnlockResult TryUnlock(string pin, string ip)
    {
        // ── 1. Rate limit check ───────────────────────────────────────────────
        if (_rate.TryGetValue(ip, out var state))
        {
            if (state.LockedUntil > DateTime.UtcNow)
                return new(UnlockStatus.LockedOut, RetryAfter: state.LockedUntil);
        }

        // ── 2. PIN validation ─────────────────────────────────────────────────
        var pinHash = config["FeatureMgmt:PinHash"];
        bool valid  = !string.IsNullOrWhiteSpace(pinHash) && hasher.Verify(pin, pinHash);

        if (!valid)
        {
            _rate.AddOrUpdate(ip, (1, DateTime.MinValue), (_, old) =>
            {
                var attempts = old.Attempts + 1;
                var lockout  = attempts >= MaxAttempts
                    ? DateTime.UtcNow.AddMinutes(LockoutMinutes)
                    : DateTime.MinValue;
                return (attempts, lockout);
            });
            return new(UnlockStatus.InvalidPin);
        }

        // ── 3. Success — clear rate limit, issue token ────────────────────────
        _rate.TryRemove(ip, out _);
        var expiry = DateTime.UtcNow.AddHours(TokenHours);
        var token  = IssueToken(expiry);
        return new(UnlockStatus.Success, Token: token, ExpiresAt: expiry);
    }

    public bool ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var secret = config["FeatureMgmt:TokenSecret"];
        if (string.IsNullOrWhiteSpace(secret)) return false;

        try
        {
            new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidateIssuer           = true,
                ValidIssuer              = Issuer,
                ValidateAudience         = true,
                ValidAudience            = Audience,
                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.Zero,
            }, out _);
            return true;
        }
        catch { return false; }
    }

    private string IssueToken(DateTime expiry)
    {
        var secret = config["FeatureMgmt:TokenSecret"]
            ?? throw new InvalidOperationException("FeatureMgmt:TokenSecret is not configured.");

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             [new Claim("purpose", "feature-access")],
            expires:            expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
