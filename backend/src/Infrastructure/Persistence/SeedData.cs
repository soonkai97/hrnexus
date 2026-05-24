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
        // Seed system roles required for authentication guards
        var systemRoles = new[] { "MANAGER", "ADMIN" };
        foreach (var roleName in systemRoles)
            if (!await db.Roles.AnyAsync(r => r.Name == roleName))
                db.Roles.Add(Role.Create(roleName, isSystem: true));

        // Seed the single default admin account
        if (!await db.Staff.AnyAsync(e => e.StaffId == "HRAdmin"))
            db.Staff.Add(Staff.Create(
                "HRAdmin", "HR", "Admin", "admin@hrnexus.com",
                hasher.Hash("Admin@1234"),
                new List<string> { "ADMIN" },
                null, null, null, null));

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

        // Seed system loan types — these two are structural and cannot be deleted
        var systemLoanTypes = new (string Key, string Label, bool IsAdvance, int Order)[]
        {
            ("ADVANCE",   "Advance (Cash Out)", true,  0),
            ("REPAYMENT", "Repayment",          false, 1),
        };
        foreach (var (key, label, isAdvance, order) in systemLoanTypes)
            if (!await db.StaffLoanTypes.AnyAsync(lt => lt.Key == key))
                db.StaffLoanTypes.Add(StaffLoanType.Create(key, label, isAdvance, order, isSystem: true));

        await db.SaveChangesAsync();
    }
}
