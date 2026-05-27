using MediatR;
using Microsoft.EntityFrameworkCore;
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

public class LoginCommandHandler(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens)
    : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand req, CancellationToken ct)
    {
        var employee = await db.Staff
            .FirstOrDefaultAsync(e => e.StaffId == req.StaffId.ToUpperInvariant() && e.IsActive, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (!hasher.Verify(req.Password, employee.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");

        // Portal access control
        var isAdmin    = employee.Roles.Any(r => r.Equals(AppConstants.Roles.Admin,   StringComparison.OrdinalIgnoreCase));
        var isElevated = isAdmin || employee.Roles.Any(r => r.Equals(AppConstants.Roles.Manager, StringComparison.OrdinalIgnoreCase));

        if (req.Portal == "staff" && isAdmin)
            throw new UnauthorizedAccessException("Admin accounts cannot access the staff portal.");

        if (req.Portal == "admin" && !isElevated)
            throw new UnauthorizedAccessException("Access denied. Manager or Admin role required.");

        var access  = tokens.GenerateAccessToken(employee);
        var refresh = tokens.GenerateRefreshToken();
        employee.SetRefreshToken(refresh, DateTime.UtcNow.AddDays(30));
        await db.SaveChangesAsync(ct);

        return new LoginResult(access, refresh, 3600,
            new UserDto(employee.Id, employee.StaffId, employee.FullName, employee.Email,
                employee.Roles.ToList()));
    }
}
