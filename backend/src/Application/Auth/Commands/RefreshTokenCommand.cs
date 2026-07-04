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

        var access = tokens.GenerateAccessToken(employee);

        // We deliberately do NOT rotate the refresh token. Rotation (issue-new + invalidate-old)
        // caused spurious logouts: if the rotated cookie response was lost in transit (a dropped
        // mobile request, backgrounded tab, killed app), the client kept the old token while the
        // server had already replaced it — so the next refresh 401'd and forced a re-login. Instead
        // we keep the same refresh token and slide its 30-day expiry, so an actively-used session
        // never expires and a lost response is harmless (the token in the cookie is still valid).
        employee.SetRefreshToken(req.RefreshToken, DateTime.UtcNow.AddDays(30));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent refresh already slid the expiry — benign now that the token isn't rotated
            // (it's unchanged and still valid), so keep the session alive instead of logging out.
        }

        return new LoginResult(access, req.RefreshToken, 30 * 60,
            UserDtoFactory.From(employee));
    }
}
