using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests.Infrastructure;

/// <summary>A clock that stands still, so a test decides what "this month" is.</summary>
public sealed class FixedClock(DateTime now) : IDateTimeProvider
{
    public DateTime UtcNow => now;
    public DateOnly TodayUtc => DateOnly.FromDateTime(now);
    public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
    public DateTime LocalNow => now;
}

/// <summary>Keeps every audit row so a test can assert what was recorded (and what was not).</summary>
public sealed class RecordingAudit : IAuditService
{
    public List<(string Type, string Id, string Action, Guid? By, string? Changes)> Entries { get; } = [];

    public Task LogAsync(string entityType, string entityId, string action, Guid? performedById, string performedByName,
        string? changes = null, CancellationToken ct = default)
    {
        Entries.Add((entityType, entityId, action, performedById, changes));
        return Task.CompletedTask;
    }
}

/// <summary>
/// A small company for payroll run tests: October 2026 is the month being paid, "now" is mid-November, and people
/// are added with a salary, a manager and the permission keys their role grants.
/// </summary>
public sealed class PayrollRunKit
{
    public const int Year = 2026;
    public const int Month = 10;

    public TestDbContext Db { get; } = new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public RecordingAudit Audit { get; } = new();
    public FixedClock Clock { get; set; } = new(new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc));

    public async Task<E.Staff> PersonAsync(string staffId, decimal basic = 3000m, Guid? reportsTo = null, string? role = null,
        string[]? keys = null, bool salary = true, DateOnly? start = null, bool dob = true)
    {
        var roleIds = new List<Guid>();
        if (role is not null) roleIds.Add(await RoleSeed.GrantAsync(Db, role, keys ?? []));
        var s = E.Staff.Create(staffId, "First", staffId, $"{staffId.ToLower()}@t.com", "hash", roleIds,
            dateOfBirth: dob ? new DateTime(1990, 3, 4) : null);
        s.SetEmployment("citizen", start ?? new DateOnly(2024, 1, 1), null);
        s.SetReportsTo(reportsTo);
        Db.Staff.Add(s);
        if (salary) Db.SalaryProfiles.Add(E.SalaryProfile.Create(s.Id, new DateOnly(2020, 1, 1), basic, "monthly", null, null, null));
        await Db.SaveChangesAsync();
        return s;
    }

    /// <summary>The preparer: may manage payroll. The head of HR: may approve. A manager: may sign off direct reports.</summary>
    public Task<E.Staff> PreparerAsync(string id = "PREP") => PersonAsync(id, role: "PAYROLL CLERK", keys: [AppConstants.Permissions.PayrollManage], salary: false);
    public Task<E.Staff> HeadOfHrAsync(string id = "HOHR", decimal basic = 6000m, bool salary = false) =>
        PersonAsync(id, basic, role: "HEAD OF HR", keys: [AppConstants.Permissions.PayrollApprove, AppConstants.Permissions.PayrollView], salary: salary);
    public Task<E.Staff> ManagerAsync(string id, Guid? reportsTo = null, bool salary = true) =>
        PersonAsync(id, 5000m, reportsTo, role: "MANAGER", keys: [AppConstants.Permissions.PayrollSignoff], salary: salary);

    public static PayrollActor Act(E.Staff s) => new(s.Id, s.FullName);

    /// <summary>Marks every seeded statutory rate row as verified, as the accountant would.</summary>
    public async Task SeedAndVerifyRatesAsync(bool verify)
    {
        foreach (var row in StatutoryRateSeed.Rows())
        {
            if (verify) row.Verify("accountant", DateTime.UtcNow, "checked");
            Db.StatutoryRateTables.Add(row);
        }
        await Db.SaveChangesAsync();
    }

    public async Task VerifyAllRatesAsync()
    {
        foreach (var row in await Db.StatutoryRateTables.Where(r => !r.Verified).ToListAsync())
            row.Verify("accountant", DateTime.UtcNow, "checked");
        await Db.SaveChangesAsync();
    }
}
