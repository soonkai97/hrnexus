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

public record LoginResult(string AccessToken, string RefreshToken, int ExpiresIn, UserDto User);

public record UserDto(Guid Id, string StaffId, string FullName, string? Email, List<string> Roles);

public class LoginCommandHandler(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, IDateTimeProvider clock)
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

        // Portal access control. Superadmins/managers are treated as normal users in the
        // staff portal (they can log their own shifts / leave there); only the admin portal
        // is gated to elevated roles.
        var isAdmin    = employee.Roles.Any(r => r.Equals(AppConstants.Roles.Admin,   StringComparison.OrdinalIgnoreCase));
        var isElevated = isAdmin || employee.Roles.Any(r => r.Equals(AppConstants.Roles.Manager, StringComparison.OrdinalIgnoreCase));

        if (req.Portal == "admin" && !isElevated)
            throw new UnauthorizedAccessException("Access denied. Manager or Admin role required.");

        // Successful auth — clear any accumulated failed-attempt state and issue tokens.
        employee.RegisterSuccessfulLogin();
        var access  = tokens.GenerateAccessToken(employee);
        var refresh = tokens.GenerateRefreshToken();
        employee.SetRefreshToken(refresh, now.AddDays(30));
        await db.SaveChangesAsync(ct);

        return new LoginResult(access, refresh, 3600,
            new UserDto(employee.Id, employee.StaffId, employee.FullName, employee.Email,
                employee.Roles.ToList()));
    }
}
