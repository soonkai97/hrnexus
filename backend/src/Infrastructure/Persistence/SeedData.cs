using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Infrastructure.Persistence;

public static class SeedData
{
    public static void Seed(ModelBuilder mb)
    {
        // Locations are seeded at runtime via SeedDemoUsersAsync to avoid
        // EF Core HasData migration pain when the entity changes.
    }

    public static async Task SeedDemoUsersAsync(AppDbContext db,
        HRNexus.Application.Common.Interfaces.IPasswordHasher hasher)
    {
        // Seed system roles required for authentication guards
        var systemRoles = new[] { AppConstants.Roles.Manager, AppConstants.Roles.Admin };
        foreach (var roleName in systemRoles)
            if (!await db.Roles.AnyAsync(r => r.Name == roleName))
                db.Roles.Add(Role.Create(roleName, isSystem: true));
        // Save so the seeded roles get their generated GUIDs before we reference them by Id below.
        await db.SaveChangesAsync();

        // Map role name → seeded Id so permissions + the admin assignment key on the GUID.
        var roleIdByName = await db.Roles
            .Where(r => systemRoles.Contains(r.Name))
            .ToDictionaryAsync(r => r.Name, r => r.Id);

        // Backfill role-default permissions: the two system roles get EVERY permission (incl.
        // portal.admin) so behaviour is identical to the pre-permission, role-gated world.
        // EMPLOYEE intentionally gets none. Idempotent + auto-seeds any newly added catalog key.
        foreach (var roleName in systemRoles)
        {
            var roleId = roleIdByName[roleName];
            foreach (var key in AppConstants.Permissions.All)
                if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionKey == key))
                    db.RolePermissions.Add(RolePermission.Create(roleId, key));
        }

        // Seed the single default admin account (StaffRole rows key on the admin role's Id).
        if (!await db.Staff.AnyAsync(e => e.StaffId == "HRADMIN"))
            db.Staff.Add(Staff.Create(
                "HRADMIN", "HR", "Admin", "admin@hrnexus.com",
                hasher.Hash("Admin@1234"),
                new List<Guid> { roleIdByName[AppConstants.Roles.Admin] },
                null, null, null, null));

        await db.SaveChangesAsync();
    }
}
