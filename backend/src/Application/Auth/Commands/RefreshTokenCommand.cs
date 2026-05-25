using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Auth.Commands;

public record RefreshTokenCommand(string RefreshToken) : IRequest<LoginResult>;

public class RefreshTokenCommandHandler(IAppDbContext db, ITokenService tokens)
    : IRequestHandler<RefreshTokenCommand, LoginResult>
{
    public async Task<LoginResult> Handle(RefreshTokenCommand req, CancellationToken ct)
    {
        var employee = await db.Staff
            .FirstOrDefaultAsync(e =>
                e.RefreshToken == req.RefreshToken &&
                e.RefreshTokenExpiry > DateTime.UtcNow &&
                e.IsActive, ct)
            ?? throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var access      = tokens.GenerateAccessToken(employee);
        var newRefresh  = tokens.GenerateRefreshToken();
        employee.SetRefreshToken(newRefresh, DateTime.UtcNow.AddDays(30));
        await db.SaveChangesAsync(ct);

        return new LoginResult(access, newRefresh, 3600,
            new UserDto(employee.Id, employee.StaffId, employee.FullName, employee.Email,
                employee.Roles.ToList()));
    }
}
