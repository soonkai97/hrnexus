using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence;

public static class SeedData
{
    public static void Seed(ModelBuilder mb)
    {
        // Locations are seeded at runtime via SeedDemoUsersAsync to avoid
        // EF Core HasData migration pain when the entity changes.
    }

    public static async Task SeedDemoUsersAsync(AppDbContext db,
        NightclubWorkforce.Application.Common.Interfaces.IPasswordHasher hasher)
    {
        // Seed default locations — use name as uniqueness key; IDs are UUIDs
        var defaultLocationNames = new[] { "MAIN BAR", "VIP LOUNGE", "MAIN STAGE", "ENTRANCE" };
        foreach (var name in defaultLocationNames)
        {
            if (!await db.Locations.AnyAsync(l => l.Name == name))
                db.Locations.Add(Location.Create(name));
        }

        // Seed default staff roles (uppercase)
        var defaultRoles = new[] { "BARTENDER", "BOUNCER", "WAITSTAFF", "DJ" };
        foreach (var roleName in defaultRoles)
            if (!await db.Roles.AnyAsync(r => r.Name == roleName))
                db.Roles.Add(Role.Create(roleName, isSystem: false));

        var systemRoles = new[] { "MANAGER", "ADMIN" };
        foreach (var roleName in systemRoles)
            if (!await db.Roles.AnyAsync(r => r.Name == roleName))
                db.Roles.Add(Role.Create(roleName, isSystem: true));

        // Seed demo employees
        var demos = new[]
        {
            (StaffId: "NC1001", First: "Marcus",  Last: "Torres",  Email: "staff@venue.com",   Roles: new List<string> { "BARTENDER" },            Phone: "+60 11-1234 5678", IcNo: "900101-01-1001", PassportNo: (string?)null, Dob: new DateTime(1990, 1,  1,  0, 0, 0, DateTimeKind.Utc)),
            (StaffId: "NC1002", First: "Alex",    Last: "Rivera",  Email: "manager@venue.com", Roles: new List<string> { "MANAGER", "BARTENDER" }, Phone: "+60 11-2345 6789", IcNo: "880515-02-2002", PassportNo: (string?)null, Dob: new DateTime(1988, 5,  15, 0, 0, 0, DateTimeKind.Utc)),
            (StaffId: "NC1003", First: "Sam",     Last: "Chen",    Email: "admin@venue.com",   Roles: new List<string> { "ADMIN" },                Phone: "+60 11-3456 7890", IcNo: "850320-03-3003", PassportNo: (string?)null, Dob: new DateTime(1985, 3,  20, 0, 0, 0, DateTimeKind.Utc)),
        };
        foreach (var d in demos)
            if (!await db.Staff.AnyAsync(e => e.Email == d.Email))
                db.Staff.Add(Staff.Create(d.StaffId, d.First, d.Last, d.Email, hasher.Hash("password123"),
                    d.Roles, d.Phone, d.IcNo, d.PassportNo, d.Dob));

        await db.SaveChangesAsync();

        // Seed default expense categories
        var defaultExpenseCategories = new (string Key, string Label, int Order)[]
        {
            ("MEAL",      "Meal Subsidy",        0),
            ("TRANSPORT", "Transport",           1),
            ("PENALTY",   "Penalty / Deduction", 2),
            ("BONUS",     "Bonus",               3),
            ("OTHER",     "Other",               4),
        };
        foreach (var (key, label, order) in defaultExpenseCategories)
            if (!await db.StaffExpenseCategories.AnyAsync(c => c.Key == key))
                db.StaffExpenseCategories.Add(StaffExpenseCategory.Create(key, label, order));

        await db.SaveChangesAsync();
    }
}
