using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Commands;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.Staff.Commands;

public record UpdateStaffCommand(
    Guid          Id,
    string        StaffId,
    string        FirstName,
    string?       LastName,
    string?       Email,
    List<string>  Roles,
    string?       PhoneNumber,
    string?       IcNo          = null,
    string?       PassportNo    = null,
    DateTime?     DateOfBirth   = null,
    List<string>? Locations     = null) : IRequest;

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

        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var emailTaken = await db.Staff.AnyAsync(
                e => e.Email == req.Email.ToLowerInvariant() && e.Id != req.Id, ct);
            if (emailTaken)
                throw new InvalidOperationException($"Email '{req.Email}' is already in use.");
        }

        // Resolve email: keep existing if not supplied
        var resolvedEmail = string.IsNullOrWhiteSpace(req.Email) ? emp.Email : req.Email;

        // Lockout guard: if this role change strips the user's admin-portal access and they're the last
        // active admin, reject — otherwise nobody could sign in to undo it. A grant/revoke override on
        // the user wins over role defaults (so changing roles can't remove access a grant pins on, and
        // can't restore access a revoke pins off).
        var key       = AppConstants.Permissions.PortalAdmin;
        var newRoles  = req.Roles.Select(r => r.Trim().ToUpperInvariant()).ToList();
        var ownOverride = await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == req.Id && o.PermissionKey == key)
            .Select(o => o.Granted).ToListAsync(ct);
        var willHaveAdmin = ownOverride.Count > 0
            ? ownOverride[0]
            : newRoles.Count > 0 && await db.RolePermissions.AnyAsync(
                  rp => newRoles.Contains(rp.RoleName) && rp.PermissionKey == key, ct);

        if (!willHaveAdmin && await PortalAdminInvariant.UserHasAdminAsync(db, req.Id, ct))
        {
            var adminRoles = await db.RolePermissions
                .Where(rp => rp.PermissionKey == key)
                .Select(rp => rp.RoleName).Distinct().ToListAsync(ct);
            if (await PortalAdminInvariant.CountActiveAdminsAsync(db, adminRoles, exclude: req.Id, ct) == 0)
                throw new ArgumentException(
                    "This change would remove admin-portal access from the last administrator. " +
                    "Grant admin-portal access to another user or role first.");
        }

        emp.UpdateDetails(normalizedId, req.FirstName, req.LastName, resolvedEmail, req.Roles,
            PhoneNormalizer.Normalize(req.PhoneNumber),
            req.IcNo, req.PassportNo, req.DateOfBirth, req.Locations);
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
