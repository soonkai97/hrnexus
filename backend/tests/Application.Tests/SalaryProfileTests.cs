using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands;
using HRNexus.Application.Payroll.Queries;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 8a (PAY-01, PAY-09): pay is a monthly salary kept as an append-only, effective-dated history;
/// employment details carry what payroll needs to prorate; salary never leaks into audit rows.
/// </summary>
public class SalaryProfileTests
{
    private sealed class CapturingAudit : IAuditService
    {
        public List<string> Changes { get; } = [];
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default)
        {
            Changes.Add($"{et}|{a}|{c}");
            return Task.CompletedTask;
        }
    }

    private sealed class StubUser(string? name) : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => name;
    }

    private sealed class Clock(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow => now;
        public DateOnly TodayUtc => DateOnly.FromDateTime(now);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Utc;
        public DateTime LocalNow => now;
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<E.Staff> AddStaff(TestDbContext db)
    {
        var roleId = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        var staff = E.Staff.Create("P1", "Pay", "Roll", "p1@t.com", "hash", new List<Guid> { roleId });
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    private static AddSalaryProfileCommand Add(Guid staffId, decimal salary = 3500m, string from = "2026-01-01",
        string frequency = "monthly", string? account = "1234567890") =>
        new(staffId, salary, frequency, DateOnly.Parse(from), "Maybank", account, "Pay Roll");

    // ── Adding a profile ──────────────────────────────────────────────────────

    [Fact]
    public async Task Add_RecordsWhoAddedTheSalary_SoPreparerAndApproverCanBeToldApart()
    {
        // Phase 8c refuses an approval by the person who prepared the run, which needs a recorded creator.
        await using var db = CreateDb();
        var staff = await AddStaff(db);

        await new AddSalaryProfileCommandHandler(db, new CapturingAudit(), new StubUser("hr.admin")).Handle(Add(staff.Id), CancellationToken.None);

        Assert.Equal("hr.admin", (await db.SalaryProfiles.SingleAsync()).CreatedBy);
    }

    [Fact]
    public async Task Add_StoresTheProfile_WithItsBankDetails()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);

        await new AddSalaryProfileCommandHandler(db, new CapturingAudit(), new StubUser("hr.admin")).Handle(Add(staff.Id), CancellationToken.None);

        var saved = await db.SalaryProfiles.SingleAsync();
        Assert.Equal(3500m, saved.BasicSalary);
        Assert.Equal("monthly", saved.PayFrequency);
        Assert.Equal(new DateOnly(2026, 1, 1), saved.EffectiveFrom);
        Assert.Equal("1234567890", saved.BankAccount);
        Assert.Equal(staff.Id, saved.EmployeeId);
    }

    [Fact]
    public async Task Add_TwiceForTheSameEffectiveDate_IsRefused_SoHistoryIsNeverRewritten()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var handler = new AddSalaryProfileCommandHandler(db, new CapturingAudit(), new StubUser("hr.admin"));
        await handler.Handle(Add(staff.Id, 3500m, "2026-01-01"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(Add(staff.Id, 4000m, "2026-01-01"), CancellationToken.None));

        Assert.Equal(3500m, (await db.SalaryProfiles.SingleAsync()).BasicSalary);
    }

    [Theory]
    [InlineData(0, "monthly")]
    [InlineData(-1, "monthly")]
    [InlineData(2_000_000, "monthly")]
    [InlineData(3500, "weekly")]
    [InlineData(3500, "hourly")]
    public async Task Add_RejectsAnInvalidSalaryOrFrequency(int salary, string frequency)
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AddSalaryProfileCommandHandler(db, new CapturingAudit(), new StubUser("hr.admin"))
                .Handle(Add(staff.Id, salary, frequency: frequency), CancellationToken.None));
    }

    [Fact]
    public async Task Add_ForAnUnknownStaff_Throws()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new AddSalaryProfileCommandHandler(db, new CapturingAudit(), new StubUser("hr.admin")).Handle(Add(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Add_NeverWritesTheSalaryOrTheAccountIntoTheAuditRow()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var audit = new CapturingAudit();

        await new AddSalaryProfileCommandHandler(db, audit, new StubUser("hr.admin")).Handle(Add(staff.Id, 4321m), CancellationToken.None);

        var row = Assert.Single(audit.Changes);
        Assert.DoesNotContain("4321", row);
        Assert.DoesNotContain("1234567890", row);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsTheProfileInEffectToday_AndTheWholeHistoryNewestFirst()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);
        var add = new AddSalaryProfileCommandHandler(db, new CapturingAudit(), new StubUser("hr.admin"));
        await add.Handle(Add(staff.Id, 3000m, "2025-01-01"), CancellationToken.None);
        await add.Handle(Add(staff.Id, 3500m, "2026-01-01"), CancellationToken.None);
        await add.Handle(Add(staff.Id, 4000m, "2027-01-01"), CancellationToken.None);   // future-dated

        var result = await new GetSalaryQueryHandler(db, new Clock(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)))
            .Handle(new GetSalaryQuery(staff.Id), CancellationToken.None);

        Assert.Equal(3500m, result.Current!.BasicSalary);   // the 2027 raise is not in effect yet
        Assert.Equal([4000m, 3500m, 3000m], result.History.Select(h => h.BasicSalary).ToArray());
    }

    [Fact]
    public async Task Get_WithNoProfile_HasNoCurrentAndAnEmptyHistory()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);

        var result = await new GetSalaryQueryHandler(db, new Clock(DateTime.UtcNow))
            .Handle(new GetSalaryQuery(staff.Id), CancellationToken.None);

        Assert.Null(result.Current);
        Assert.Empty(result.History);
    }

    [Fact]
    public async Task Get_ForAnUnknownStaff_Throws()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetSalaryQueryHandler(db, new Clock(DateTime.UtcNow)).Handle(new GetSalaryQuery(Guid.NewGuid()), CancellationToken.None));
    }

    // ── Employment details ────────────────────────────────────────────────────

    [Fact]
    public async Task SetEmployment_StoresTheDetails_AndGetReturnsThem()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);

        await new SetEmploymentDetailsCommandHandler(db, new CapturingAudit()).Handle(
            new SetEmploymentDetailsCommand(staff.Id, "citizen", new DateOnly(2025, 3, 3), null), CancellationToken.None);

        var result = await new GetSalaryQueryHandler(db, new Clock(DateTime.UtcNow))
            .Handle(new GetSalaryQuery(staff.Id), CancellationToken.None);
        Assert.Equal("citizen", result.Employment.CitizenshipStatus);
        Assert.Equal(new DateOnly(2025, 3, 3), result.Employment.StartDate);
        Assert.Null(result.Employment.EndDate);
    }

    [Theory]
    [InlineData("foreigner")]
    [InlineData("")]
    public async Task SetEmployment_RejectsAnUnsupportedCitizenshipStatus(string status)
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetEmploymentDetailsCommandHandler(db, new CapturingAudit()).Handle(
                new SetEmploymentDetailsCommand(staff.Id, status, new DateOnly(2025, 3, 3), null), CancellationToken.None));
    }

    [Fact]
    public async Task SetEmployment_RejectsAnEndDateBeforeTheStartDate()
    {
        await using var db = CreateDb();
        var staff = await AddStaff(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetEmploymentDetailsCommandHandler(db, new CapturingAudit()).Handle(
                new SetEmploymentDetailsCommand(staff.Id, "permanent-resident", new DateOnly(2025, 3, 3), new DateOnly(2025, 3, 2)), CancellationToken.None));
    }
}
