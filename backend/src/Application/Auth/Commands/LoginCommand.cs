using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.Auth.Commands;

/// <summary>
/// Portal = "staff"  → staff app (admins blocked).
/// Portal = "admin"  → admin app (only manager/admin allowed).
/// Portal = null     → no portal restriction (backwards compat / API testing).
/// </summary>
public record LoginCommand(string StaffId, string Password, string? Portal = null) : IRequest<LoginResult>;

public record LoginResult(
    string AccessToken, string RefreshToken, int ExpiresIn, UserDto User,
    bool RequiresTwoFactor = false, string? TwoFactorToken = null);

public record UserDto(Guid Id, string StaffId, string FullName, string? Email, List<string> Roles);

public class LoginCommandHandler(
    IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, IDateTimeProvider clock,
    IPermissionResolver permissions)
    : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand req, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var employee = await db.Staff
            .FirstOrDefaultAsync(e => e.StaffId == req.StaffId.ToUpperInvariant() && e.IsActive, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        // Brute-force lockout: a locked account is refused outright, before any password check,
        // so attackers can't keep probing. Returns 429 so the client can show "try again later".
        if (employee.IsLockedOut(now))
            throw new TooManyRequestsException(
                "Account temporarily locked after too many failed attempts. Please try again later.");

        if (!hasher.Verify(req.Password, employee.PasswordHash))
        {
            // Count the failure and persist it. If this attempt crossed the threshold, surface the
            // lockout message (429) instead of the generic 401 so the user understands the cooldown.
            var justLocked = employee.RegisterFailedLogin(
                now, AppConstants.Lockout.MaxFailedAttempts, AppConstants.Lockout.Duration);
            await db.SaveChangesAsync(ct);

            if (justLocked)
                throw new TooManyRequestsException(
                    "Account temporarily locked after too many failed attempts. Please try again later.");
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // Portal access control. The staff portal is open to all staff (admins/managers can log
        // their own shifts/leave there). The admin portal is gated by the portal.admin PERMISSION
        // (role-default or per-user override), resolved fresh from the DB so a revocation takes
        // effect on the next login rather than waiting out the token lifetime.
        if (req.Portal == "admin" &&
            !await permissions.HasAsync(employee.Id, AppConstants.Permissions.PortalAdmin, ct))
            throw new UnauthorizedAccessException("Access denied. You do not have admin portal access.");

        // Password is correct — clear any accumulated failed-attempt state.
        employee.RegisterSuccessfulLogin();

        // Admin-portal 2FA challenge: with 2FA enabled, the correct password alone is NOT enough.
        // Return a short-lived challenge token; the real session cookies are only issued once the
        // user passes /api/auth/2fa/verify. (Staff-portal logins are not gated by 2FA.)
        if (req.Portal == "admin" && employee.TwoFactorEnabled)
        {
            await db.SaveChangesAsync(ct);   // persist the lockout reset
            return new LoginResult("", "", 0,
                new UserDto(employee.Id, employee.StaffId, employee.FullName, employee.Email,
                    employee.Roles.ToList()),
                RequiresTwoFactor: true,
                TwoFactorToken: tokens.GenerateTwoFactorToken(employee.Id));
        }

        // No 2FA — issue session tokens immediately.
        var access  = tokens.GenerateAccessToken(employee);
        var refresh = tokens.GenerateRefreshToken();
        employee.SetRefreshToken(refresh, now.AddDays(30));
        await db.SaveChangesAsync(ct);

        return new LoginResult(access, refresh, 3600,
            new UserDto(employee.Id, employee.StaffId, employee.FullName, employee.Email,
                employee.Roles.ToList()));
    }
}
