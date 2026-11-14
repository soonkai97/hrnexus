using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Staff.Commands;

// Bulk staff actions — each operates on a selected set of staff at once. They mirror the
// BulkUpdateLeaveStatusCommand shape: validate a ≤100 batch, load all rows, mutate, save once,
// audit each affected row in the same transaction. Assignment is ADDITIVE (union) — bulk-assign
// adds roles on top of each staff member's existing ones rather than replacing them.

public record BulkAssignRolesCommand(List<Guid> StaffIds, List<Guid> Roles) : IRequest<int>;
public record BulkDeactivateStaffCommand(List<Guid> StaffIds) : IRequest<int>;

public class BulkAssignRolesCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<BulkAssignRolesCommand, int>
{
    public async Task<int> Handle(BulkAssignRolesCommand req, CancellationToken ct)
    {
        if (req.StaffIds.Count == 0 || req.Roles.Count == 0) return 0;
        if (req.StaffIds.Count > 100)
            throw new ArgumentException("Cannot bulk-update more than 100 staff at once.");

        var roles = req.Roles.Where(r => r != Guid.Empty).Distinct().ToList();
        var validCount = await db.Roles.CountAsync(r => roles.Contains(r.Id), ct);
        if (validCount != roles.Count)
            throw new ArgumentException("One or more selected roles no longer exist.");

        var staff = await db.Staff.Where(s => req.StaffIds.Contains(s.Id)).ToListAsync(ct);
        foreach (var emp in staff)
        {
            emp.AddRoles(roles);
            await audit.LogAsync("Staff", emp.Id.ToString(), "BulkAssignRoles",
                null, "Admin", $"+{roles.Count} role(s)", ct);
        }
        await db.SaveChangesAsync(ct);
        return staff.Count;
    }
}

public class BulkDeactivateStaffCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<BulkDeactivateStaffCommand, int>
{
    public async Task<int> Handle(BulkDeactivateStaffCommand req, CancellationToken ct)
    {
        if (req.StaffIds.Count == 0) return 0;
        if (req.StaffIds.Count > 100)
            throw new ArgumentException("Cannot bulk-update more than 100 staff at once.");

        // Only act on currently-active staff (deactivating an inactive account is a no-op).
        var staff = await db.Staff.Where(s => req.StaffIds.Contains(s.Id) && s.IsActive).ToListAsync(ct);
        if (staff.Count == 0) return 0;

        // Whole-selection last-admin guard: if EVERY active user who can manage roles and permissions is in this batch,
        // deactivating them all would lock everyone out. The per-row guard can't see this — it would
        // pass each one because the others are still committed-active until the single SaveChanges.
        var adminRoles = await db.RolePermissions
            .Where(rp => rp.PermissionKey == AppConstants.Permissions.RolesManage)
            .Select(rp => rp.RoleId).Distinct().ToListAsync(ct);
        var totalActiveAdmins = await RolesManageInvariant.CountActiveAdminsAsync(db, adminRoles, exclude: null, ct);
        var selectedAdmins = 0;
        foreach (var emp in staff)
            if (await RolesManageInvariant.UserHasAdminAsync(db, emp.Id, ct)) selectedAdmins++;
        if (selectedAdmins > 0 && totalActiveAdmins - selectedAdmins < 1)
            throw new ArgumentException(
                "This selection includes every active user with role-management access — deactivating them " +
                "would lock everyone out. Keep at least one administrator active.");

        foreach (var emp in staff)
        {
            emp.Deactivate();
            await audit.LogAsync("Staff", emp.Id.ToString(), "BulkDeactivate", null, "Admin", null, ct);
        }
        await db.SaveChangesAsync(ct);
        return staff.Count;
    }
}
