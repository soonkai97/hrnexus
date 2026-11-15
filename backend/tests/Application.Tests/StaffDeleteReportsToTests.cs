using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// WHY these tests matter: a manager can sign off the pay of the people who report to them. Deleting a person is a
/// soft delete, so the database never clears that link by itself; without this, a deleted manager's reports would
/// keep pointing at them, and an undeleted or re-created account would silently get the signing authority back.
/// </summary>
public class StaffDeleteReportsToTests
{
    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<E.Staff> AddAsync(TestDbContext db, string staffId, Guid? reportsTo = null)
    {
        var s = E.Staff.Create(staffId, "A", "B", $"{staffId.ToLower()}@t.com", "hash", []);
        s.SetReportsTo(reportsTo);
        db.Staff.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Deleting_a_manager_leaves_their_reports_with_no_manager_and_others_untouched()
    {
        await using var db = CreateDb();
        var ceo = await AddAsync(db, "CEO1");
        var manager = await AddAsync(db, "MGR1", ceo.Id);
        var worker = await AddAsync(db, "WRK1", manager.Id);
        var otherWorker = await AddAsync(db, "WRK2", ceo.Id);

        await new DeleteStaffCommandHandler(db).Handle(new DeleteStaffCommand(manager.Id), CancellationToken.None);

        Assert.Null((await db.Staff.FindAsync(worker.Id))!.ReportsToId);
        Assert.Equal(ceo.Id, (await db.Staff.FindAsync(otherWorker.Id))!.ReportsToId);
    }
}
