using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Commands;

public record AdminResetPasswordCommand(Guid EmployeeId, string NewPassword) : IRequest;

public class AdminResetPasswordCommandHandler(IAppDbContext db, IPasswordHasher hasher)
    : IRequestHandler<AdminResetPasswordCommand>
{
    public async Task Handle(AdminResetPasswordCommand req, CancellationToken ct)
    {
        var emp = await db.Staff.FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {req.EmployeeId} not found.");

        emp.SetPasswordHash(hasher.Hash(req.NewPassword));
        emp.ClearRefreshToken();
        await db.SaveChangesAsync(ct);
    }
}
