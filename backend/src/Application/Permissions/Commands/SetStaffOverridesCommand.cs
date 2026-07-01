using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Queries;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Permissions.Commands;

/// <summary>Replace a user's per-user permission overrides (replace-all semantics).</summary>
public record SetStaffOverridesCommand(Guid EmployeeId, List<StaffOverrideDto> Overrides) : IRequest;

public class SetStaffOverridesHandler(IAppDbContext db, IPermissionResolver resolver)
    : IRequestHandler<SetStaffOverridesCommand>
{
    public async Task Handle(SetStaffOverridesCommand req, CancellationToken ct)
    {
        if (!await db.Staff.AnyAsync(s => s.Id == req.EmployeeId, ct))
            throw new KeyNotFoundException("Staff member not found.");

        var overrides = req.Overrides
            .GroupBy(o => o.PermissionKey)
            .Select(g => g.Last())   // last write wins if a key is sent twice
            .ToList();

        var unknown = overrides.Where(o => !AppConstants.Permissions.All.Contains(o.PermissionKey))
            .Select(o => o.PermissionKey).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown permission(s): {string.Join(", ", unknown)}");

        // Lockout guard: if this edit removes the user's OWN admin-portal access and they currently have
        // it, ensure at least one OTHER active user still does (else nobody could log in to undo it).
        var adminOverride = overrides.FirstOrDefault(o => o.PermissionKey == AppConstants.Permissions.PortalAdmin);
        bool userKeepsAdmin;
        if (adminOverride is not null)
        {
            userKeepsAdmin = adminOverride.Granted;   // explicit grant keeps it; explicit revoke drops it
        }
        else
        {
            // No explicit override after this change → falls back to the user's role defaults.
            var userRoles = await db.StaffRoles.Where(sr => sr.EmployeeId == req.EmployeeId)
                .Select(sr => sr.RoleId).ToListAsync(ct);
            userKeepsAdmin = userRoles.Count > 0
                && await db.RolePermissions.AnyAsync(
                    rp => userRoles.Contains(rp.RoleId)
                       && rp.PermissionKey == AppConstants.Permissions.PortalAdmin, ct);
        }

        if (!userKeepsAdmin && await PortalAdminInvariant.UserHasAdminAsync(db, req.EmployeeId, ct))
        {
            var adminRoles = await db.RolePermissions
                .Where(rp => rp.PermissionKey == AppConstants.Permissions.PortalAdmin)
                .Select(rp => rp.RoleId).Distinct().ToListAsync(ct);
            if (await PortalAdminInvariant.CountActiveAdminsAsync(db, adminRoles, exclude: req.EmployeeId, ct) == 0)
                throw new ArgumentException(
                    "This change would remove admin-portal access from the last administrator. " +
                    "Grant 'Admin portal access' to another user or role first.");
        }

        var existing = await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == req.EmployeeId).ToListAsync(ct);
        db.StaffPermissionOverrides.RemoveRange(existing);
        foreach (var o in overrides)
            db.StaffPermissionOverrides.Add(
                StaffPermissionOverride.Create(req.EmployeeId, o.PermissionKey, o.Granted));

        await db.SaveChangesAsync(ct);
        resolver.Invalidate(req.EmployeeId);
    }
}
