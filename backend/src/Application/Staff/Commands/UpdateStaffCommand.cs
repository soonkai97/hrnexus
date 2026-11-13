using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Staff.Commands;

public record UpdateStaffCommand(
    Guid          Id,
    string        StaffId,
    string        FirstName,
    string?       LastName,
    string?       Email,
    List<Guid>    Roles,
    string?       PhoneNumber,
    string?       IcNo          = null,
    string?       PassportNo    = null,
    DateTime?     DateOfBirth   = null) : IRequest;

public class UpdateStaffCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<UpdateStaffCommand>
{
    public async Task Handle(UpdateStaffCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FirstName)) throw new ArgumentException("First name is required.");

        // FirstOrDefaultAsync (not FindAsync) so that AutoInclude on RoleEntries is honoured.
        var emp = await db.Staff.FirstOrDefaultAsync(e => e.Id == req.Id, ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");

        var normalizedId = req.StaffId.Trim().ToUpperInvariant();

        var idTaken = await db.Staff.AnyAsync(
            e => e.StaffId == normalizedId && e.Id != req.Id, ct);
        if (idTaken)
            throw new InvalidOperationException($"Staff ID '{normalizedId}' is already in use.");

        // Lockout guard: if this role change strips the user's role-management access and they're the last
        // active admin, reject — otherwise nobody could sign in to undo it. A grant/revoke override on
        // the user wins over role defaults (so changing roles can't remove access a grant pins on, and
        // can't restore access a revoke pins off).
        var key       = AppConstants.Permissions.RolesManage;
        var newRoles  = req.Roles.Where(r => r != Guid.Empty).ToList();
        var ownOverride = await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == req.Id && o.PermissionKey == key)
            .Select(o => o.Granted).ToListAsync(ct);
        var willHaveAdmin = ownOverride.Count > 0
            ? ownOverride[0]
            : newRoles.Count > 0 && await db.RolePermissions.AnyAsync(
                  rp => newRoles.Contains(rp.RoleId) && rp.PermissionKey == key, ct);

        if (!willHaveAdmin && await RolesManageInvariant.UserHasAdminAsync(db, req.Id, ct))
        {
            var adminRoles = await db.RolePermissions
                .Where(rp => rp.PermissionKey == key)
                .Select(rp => rp.RoleId).Distinct().ToListAsync(ct);
            if (await RolesManageInvariant.CountActiveAdminsAsync(db, adminRoles, exclude: req.Id, ct) == 0)
                throw new ArgumentException(
                    "This change would remove role-management access from the last administrator. " +
                    "Grant role-management access to another user or role first.");
        }

        // Email is a full-update field: pass it straight to the entity, which distinguishes null
        // ("field not supplied → keep existing") from "" / whitespace ("cleared → set to null").
        // This lets the admin edit form clear an email, consistent with the other optional fields.
        emp.UpdateDetails(normalizedId, req.FirstName, req.LastName, req.Email, req.Roles,
            PhoneNormalizer.Normalize(req.PhoneNumber),
            req.IcNo, req.PassportNo, req.DateOfBirth);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Unique constraint race: another request claimed the same StaffId or Email
            // between our AnyAsync checks and this save. Re-check to give a precise message.
            var idStillTaken = await db.Staff.AnyAsync(e => e.StaffId == normalizedId && e.Id != req.Id, ct);
            if (idStillTaken)
                throw new InvalidOperationException($"Staff ID '{normalizedId}' is already in use.");
            throw new InvalidOperationException($"Email '{req.Email}' is already in use.");
        }
        await audit.LogAsync("Staff", req.Id.ToString(), "Updated",
            null, "Admin", $"StaffId={normalizedId}", ct);
    }
}
