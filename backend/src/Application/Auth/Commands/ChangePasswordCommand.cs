using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Auth.Commands;

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

        if (req.NewPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        employee.ChangePassword(hasher.Hash(req.NewPassword));
        await db.SaveChangesAsync(ct);
    }
}
