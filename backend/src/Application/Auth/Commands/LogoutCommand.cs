using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Auth.Commands;

/// <summary>
/// Invalidates a session server-side by clearing the staff member's stored refresh token,
/// looked up by the opaque refresh-token value carried in the logout request's cookie.
/// Tolerant: a no-op if the token doesn't match any active session (already logged out / rotated).
/// </summary>
public record LogoutCommand(string RefreshToken) : IRequest;

public class LogoutCommandHandler(IAppDbContext db) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand req, CancellationToken ct)
    {
        var employee = await db.Staff
            .FirstOrDefaultAsync(e => e.RefreshToken == req.RefreshToken, ct);
        if (employee is null) return;

        employee.ClearRefreshToken();
        await db.SaveChangesAsync(ct);
    }
}
