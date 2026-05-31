using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using HRNexus.Domain.Constants;

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

        // Lockout guard: if this person is the last active user with admin-portal access, refuse to
        // deactivate them — otherwise nobody could sign in to reactivate anyone.
        if (await PortalAdminInvariant.UserHasAdminAsync(db, req.Id, ct))
        {
            var adminRoles = await db.RolePermissions
                .Where(rp => rp.PermissionKey == AppConstants.Permissions.PortalAdmin)
                .Select(rp => rp.RoleName).Distinct().ToListAsync(ct);
            if (await PortalAdminInvariant.CountActiveAdminsAsync(db, adminRoles, exclude: req.Id, ct) == 0)
                throw new ArgumentException(
                    "This is the last active user with admin-portal access — deactivating them would " +
                    "lock everyone out. Grant admin-portal access to another user or role first.");
        }

        emp.Deactivate();
        await db.SaveChangesAsync(ct);
    }
}
