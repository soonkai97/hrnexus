using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Application.WorkLog.Queries;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// The staff clock-in cards show the job in the viewer's language. A WorkLog only stores the frozen
/// English RoleName snapshot, so the read side resolves the CURRENT localized names by matching that
/// snapshot back to the Roles table. These tests pin that resolution — including the deliberate
/// null-fallback when no role matches (deleted/renamed job) so the client shows the snapshot.
/// </summary>
public class WorkLogLocalizationTests
{
    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    [Fact]
    public async Task GetMyWorkLogs_ResolvesLocalizedNames_FromRoleNameSnapshot()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        // Job with Chinese + Vietnamese display names; Name is stored UPPERCASE.
        db.Roles.Add(E.Role.Create("Overnight 8H", nameZh: "包夜8小时", nameVi: "Qua đêm 8 giờ"));
        // A closed shift logged under that job's (uppercased) snapshot name.
        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "Overnight 8H", "loc1", "MAIN",
            DateOnly.FromDateTime(DateTime.UtcNow.Date), "22:00", "06:00", 50m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetMyWorkLogsQueryHandler(db);
        var result = await handler.Handle(new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal("OVERNIGHT 8H", dto.RoleName);   // the frozen snapshot, unchanged
        Assert.Equal("包夜8小时", dto.RoleNameZh);
        Assert.Equal("Qua đêm 8 giờ", dto.RoleNameVi);
    }

    [Fact]
    public async Task GetMyWorkLogs_LeavesLocalizedNamesNull_WhenNoRoleMatches()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        // No matching Role row exists for this snapshot (the job was since deleted or renamed).
        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "Ghost Job", "loc1", "MAIN",
            DateOnly.FromDateTime(DateTime.UtcNow.Date), "09:00", "17:00", 20m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetMyWorkLogsQueryHandler(db);
        var result = await handler.Handle(new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal("GHOST JOB", dto.RoleName);      // client falls back to this
        Assert.Null(dto.RoleNameZh);
        Assert.Null(dto.RoleNameVi);
    }

    [Fact]
    public async Task GetActiveWorkLog_ResolvesLocalizedNames()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        db.Roles.Add(E.Role.Create("Saigon Run 1H", nameZh: "跑台Saigon1小时", nameVi: "Chạy bàn Saigon 1 giờ"));
        db.WorkLogs.Add(E.WorkLog.StartLive(empId, "Saigon Run 1H", "loc1", "MAIN",
            DateOnly.FromDateTime(DateTime.UtcNow.Date), "20:00", 25m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetActiveWorkLogQueryHandler(db);
        var dto = await handler.Handle(new GetActiveWorkLogQuery(empId), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("跑台Saigon1小时", dto!.RoleNameZh);
        Assert.Equal("Chạy bàn Saigon 1 giờ", dto.RoleNameVi);
    }

    [Fact]
    public async Task GetMyWorkLogs_ResolvesLocalizedLocationNames_FromLocationId()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        // A location with Chinese + Vietnamese display names; the WorkLog is keyed by its real id.
        var loc = E.Location.Create("Main Floor", nameZh: "主楼层", nameVi: "Tầng chính");
        db.Locations.Add(loc);
        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "Runner", loc.Id, loc.Name,
            DateOnly.FromDateTime(DateTime.UtcNow.Date), "22:00", "06:00", 50m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetMyWorkLogsQueryHandler(db);
        var result = await handler.Handle(new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal("主楼层", dto.LocationNameZh);
        Assert.Equal("Tầng chính", dto.LocationNameVi);
    }

    [Fact]
    public async Task GetMyWorkLogs_LeavesLocalizedLocationNamesNull_WhenLocationIdDoesNotMatch()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();
        // A location exists, but the WorkLog is keyed by an unrelated id (deleted/renamed location).
        db.Locations.Add(E.Location.Create("Main Floor", nameZh: "主楼层", nameVi: "Tầng chính"));
        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "Runner", "ghost-location-id", "MAIN FLOOR",
            DateOnly.FromDateTime(DateTime.UtcNow.Date), "22:00", "06:00", 50m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetMyWorkLogsQueryHandler(db);
        var result = await handler.Handle(new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal("MAIN FLOOR", dto.LocationName);   // client falls back to the frozen snapshot
        Assert.Null(dto.LocationNameZh);
        Assert.Null(dto.LocationNameVi);
    }
}
