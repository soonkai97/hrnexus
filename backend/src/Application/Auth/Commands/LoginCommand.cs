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

/// <summary>An assigned job/role, carrying the localizable display names so the staff-app
/// log-shift dropdown can render a language-aware label and submit the role's GUID id.</summary>
public record JobDto(Guid Id, string Name, string? NameZh, string? NameVi);

public record UserDto(Guid Id, string StaffId, string FullName, string? Email, List<string> Roles, List<JobDto> Jobs);

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

        // Successful auth — clear any accumulated failed-attempt state and issue tokens.
        employee.RegisterSuccessfulLogin();
        var access  = tokens.GenerateAccessToken(employee);
        var refresh = tokens.GenerateRefreshToken();
        employee.SetRefreshToken(refresh, now.AddDays(30));
        await db.SaveChangesAsync(ct);

        return new LoginResult(access, refresh, 1800,   // ~access-token lifetime (30 min); FE refreshes reactively on 401
            UserDtoFactory.From(employee));
    }
}

/// <summary>Builds the login/refresh <see cref="UserDto"/> from a loaded Staff (with RoleEntries +
/// the AutoInclude'd Role nav). Keeps <c>Roles</c> as role NAMES for the FE auth-tier logic, and
/// adds <c>Jobs</c> (id + localizable names) for the staff-app log-shift role dropdown.</summary>
public static class UserDtoFactory
{
    public static UserDto From(HRNexus.Domain.Entities.Staff employee) =>
        new(employee.Id, employee.StaffId, employee.FullName, employee.Email,
            employee.Roles.ToList(),
            employee.RoleEntries
                .Where(re => re.Role != null)
                .Select(re => new JobDto(re.RoleId, re.Role.Name, re.Role.NameZh, re.Role.NameVi))
                .ToList());
}
