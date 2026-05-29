using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Auth.Commands;

public record ChangePasswordCommand(Guid EmployeeId, string CurrentPassword, string NewPassword) : IRequest;

public class ChangePasswordCommandHandler(IAppDbContext db, IPasswordHasher hasher)
    : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand req, CancellationToken ct)
    {
        var employee = await db.Staff.FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct)
            ?? throw new KeyNotFoundException("Employee not found.");

        if (!hasher.Verify(req.CurrentPassword, employee.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        if (string.IsNullOrWhiteSpace(req.NewPassword))
            throw new ArgumentException("New password is required.");

        if (req.NewPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        if (!req.NewPassword.Any(char.IsUpper) || !req.NewPassword.Any(char.IsDigit))
            throw new ArgumentException("Password must include at least one uppercase letter and one number.");

        employee.ChangePassword(hasher.Hash(req.NewPassword));
        employee.ClearRefreshToken();
        await db.SaveChangesAsync(ct);
    }
}
