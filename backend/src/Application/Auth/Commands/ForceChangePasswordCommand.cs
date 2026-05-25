using MediatR;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Auth.Commands;

/// <summary>
/// Sets a new password without requiring the old one.
/// Used when <see cref="Domain.Entities.Staff.MustChangePassword"/> is true (post temp-password login).
/// </summary>
public record ForceChangePasswordCommand(Guid EmployeeId, string NewPassword) : IRequest;

public class ForceChangePasswordCommandHandler(IAppDbContext db, IPasswordHasher hasher)
    : IRequestHandler<ForceChangePasswordCommand>
{
    public async Task Handle(ForceChangePasswordCommand req, CancellationToken ct)
    {
        var emp = await db.Staff.FindAsync([req.EmployeeId], ct)
            ?? throw new KeyNotFoundException("Employee not found.");

        if (req.NewPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        emp.SetPasswordHash(hasher.Hash(req.NewPassword));
        emp.ClearMustChangePassword();
        await db.SaveChangesAsync(ct);
    }
}
