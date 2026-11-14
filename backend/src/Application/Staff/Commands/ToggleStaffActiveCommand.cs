using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using MediatR;

namespace HRNexus.Application.Staff.Commands;

public record ActivateStaffCommand(Guid Id) : IRequest;
public record DeactivateStaffCommand(Guid Id) : IRequest;

public class ActivateStaffCommandHandler(IAppDbContext db) : IRequestHandler<ActivateStaffCommand>
{
    public async Task Handle(ActivateStaffCommand req, CancellationToken ct)
    {
        var emp = await db.Staff.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");
        emp.Activate();
        await db.SaveChangesAsync(ct);
    }
}

public class DeactivateStaffCommandHandler(IAppDbContext db) : IRequestHandler<DeactivateStaffCommand>
{
    public async Task Handle(DeactivateStaffCommand req, CancellationToken ct)
    {
        var emp = await db.Staff.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");

        // Lockout guard: if this person is the last active user with role-management access, refuse to
        // deactivate them — otherwise nobody could sign in to reactivate anyone.
        await RolesManageInvariant.EnsureNotTheLastAsync(db, req.Id, "deactivating", ct);

        emp.Deactivate();
        await db.SaveChangesAsync(ct);
    }
}
