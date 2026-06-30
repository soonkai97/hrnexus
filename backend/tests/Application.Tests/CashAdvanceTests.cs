using Microsoft.EntityFrameworkCore;
using HRNexus.Application.CashAdvance.Commands;
using HRNexus.Application.CashAdvance.Queries;
using HRNexus.Application.Tests.Infrastructure;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Cash Advance (admin-records-only). WHY these matter:
///  - an advance is money owed, so a non-positive amount or an unknown staff member must be rejected;
///  - settle/reopen drive the "outstanding" total the payroll view relies on for deductions;
///  - the staff read-only query must never leak another employee's advances.
/// </summary>
public class CashAdvanceTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<E.Staff> SeedStaff(TestDbContext db, string staffId = "S001", string first = "Alice")
    {
        var s = E.Staff.Create(staffId, first, "Wong", $"{staffId}@test.com", "hash", new List<string> { "BARTENDER" });
        db.Staff.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    private static readonly DateOnly Day = new(2026, 6, 30);

    [Fact]
    public async Task Create_records_an_outstanding_advance()
    {
        await using var db = CreateDb();
        var staff = await SeedStaff(db);

        var id = await new CreateCashAdvanceCommandHandler(db)
            .Handle(new CreateCashAdvanceCommand(staff.Id, 200m, Day, "Rent", "Manager"), default);

        var adv = await db.CashAdvances.SingleAsync();
        Assert.Equal(id, adv.Id);
        Assert.Equal(200m, adv.Amount);
        Assert.Equal(E.CashAdvance.Outstanding, adv.Status);
        Assert.Null(adv.SettledAt);
        Assert.Equal("Manager", adv.CreatedBy);
    }

    [Fact]
    public async Task Create_rejects_a_non_positive_amount()
    {
        await using var db = CreateDb();
        var staff = await SeedStaff(db);
        var handler = new CreateCashAdvanceCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateCashAdvanceCommand(staff.Id, 0m, Day, null, "Manager"), default));
    }

    [Fact]
    public async Task Create_throws_when_the_staff_member_does_not_exist()
    {
        await using var db = CreateDb();
        var handler = new CreateCashAdvanceCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new CreateCashAdvanceCommand(Guid.NewGuid(), 100m, Day, null, "Manager"), default));
    }

    [Fact]
    public async Task Settle_then_reopen_toggles_status_and_timestamp()
    {
        await using var db = CreateDb();
        var staff = await SeedStaff(db);
        var id = await new CreateCashAdvanceCommandHandler(db)
            .Handle(new CreateCashAdvanceCommand(staff.Id, 150m, Day, null, "Manager"), default);
        var settle = new SetCashAdvanceSettledCommandHandler(db);

        await settle.Handle(new SetCashAdvanceSettledCommand(id, true), default);
        var adv = await db.CashAdvances.SingleAsync();
        Assert.Equal(E.CashAdvance.Settled, adv.Status);
        Assert.NotNull(adv.SettledAt);

        await settle.Handle(new SetCashAdvanceSettledCommand(id, false), default);
        adv = await db.CashAdvances.SingleAsync();
        Assert.Equal(E.CashAdvance.Outstanding, adv.Status);
        Assert.Null(adv.SettledAt);
    }

    [Fact]
    public async Task Delete_removes_the_record()
    {
        await using var db = CreateDb();
        var staff = await SeedStaff(db);
        var id = await new CreateCashAdvanceCommandHandler(db)
            .Handle(new CreateCashAdvanceCommand(staff.Id, 80m, Day, null, "Manager"), default);

        await new DeleteCashAdvanceCommandHandler(db).Handle(new DeleteCashAdvanceCommand(id), default);

        Assert.Equal(0, await db.CashAdvances.CountAsync());
    }

    [Fact]
    public async Task Admin_query_returns_staff_name_and_honours_the_status_filter()
    {
        await using var db = CreateDb();
        var staff = await SeedStaff(db);
        var create = new CreateCashAdvanceCommandHandler(db);
        var a1 = await create.Handle(new CreateCashAdvanceCommand(staff.Id, 100m, Day, null, "Manager"), default);
        await create.Handle(new CreateCashAdvanceCommand(staff.Id, 50m, Day, null, "Manager"), default);
        await new SetCashAdvanceSettledCommandHandler(db).Handle(new SetCashAdvanceSettledCommand(a1, true), default);

        var query = new GetCashAdvancesQueryHandler(db);

        var all = await query.Handle(new GetCashAdvancesQuery(null), default);
        Assert.Equal(2, all.Count);
        Assert.Contains("Alice", all[0].StaffName);
        Assert.Equal("S001", all[0].StaffId);

        var outstanding = await query.Handle(new GetCashAdvancesQuery(E.CashAdvance.Outstanding), default);
        Assert.Single(outstanding);
        Assert.Equal(50m, outstanding[0].Amount);
    }

}
