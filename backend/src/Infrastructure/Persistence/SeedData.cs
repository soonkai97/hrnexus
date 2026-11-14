using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Infrastructure.Persistence;

public static class SeedData
{
    /// <summary>
    /// The name given to the first administrator job when a database has none. It is only a name: nothing
    /// looks the job up by it afterwards (the job is found by its <c>IsSystem</c> flag), so it can be renamed.
    /// </summary>
    public const string BootstrapRoleName = "ADMINISTRATOR";

    public static void Seed(ModelBuilder mb)
    {
        // Reference data is seeded at runtime via SeedDemoUsersAsync to avoid
        // EF Core HasData migration pain when an entity changes.
    }

    public static async Task SeedDemoUsersAsync(AppDbContext db,
        HRNexus.Application.Common.Interfaces.IPasswordHasher hasher)
    {
        // The first administrator job: the one job flagged IsSystem. If a database has several (older data),
        // the oldest wins. If none is flagged, an existing job already called ADMINISTRATOR is flagged
        // (never inserted twice: the name is unique), otherwise the job is created.
        var bootstrap = await db.Roles.Where(r => r.IsSystem)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Name).FirstOrDefaultAsync();
        if (bootstrap is null)
        {
            bootstrap = await db.Roles.FirstOrDefaultAsync(r => r.Name == BootstrapRoleName);
            if (bootstrap is null)
            {
                bootstrap = Role.Create(BootstrapRoleName, isSystem: true);
                db.Roles.Add(bootstrap);
            }
            else
            {
                bootstrap.MarkSystem();
            }
        }
        // Save so a newly created job has its id before permissions and the account reference it.
        await db.SaveChangesAsync();

        // It always holds EVERY permission (the roles page does not let anyone edit it), and a permission added
        // to the catalog later is granted here at the next start, so the first administrator is never locked out.
        var held = await db.RolePermissions.Where(rp => rp.RoleId == bootstrap.Id)
            .Select(rp => rp.PermissionKey).ToListAsync();
        foreach (var key in AppConstants.Permissions.All.Except(held))
            db.RolePermissions.Add(RolePermission.Create(bootstrap.Id, key));

        // The documented first-run account is created ONLY in an empty database (counting deleted accounts
        // too). A deleted or renamed HRADMIN therefore never brings the default password back.
        if (!await db.Staff.IgnoreQueryFilters().AnyAsync())
            db.Staff.Add(Staff.Create(
                "HRADMIN", "HR", "Admin", "admin@hrnexus.com",
                hasher.Hash("Admin@1234"),
                new List<Guid> { bootstrap.Id },
                null, null, null, null));

        await db.SaveChangesAsync();
    }
}
