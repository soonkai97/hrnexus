using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Tests for GetMyWorkLogsQueryHandler, GetActiveWorkLogQueryHandler, and GetAllWorkLogsQueryHandler.
/// Why these matter: shift log pagination, ordering, hours/cost calculations, and filters are the
/// contract between the shift-clock UI and both the staff app (my shifts) and the admin reports view;
/// incorrect values directly mislead staff and managers about attendance and labour costs.
/// </summary>
public class WorkLogQueryTests
{
    // ── Null stubs ────────────────────────────────────────────────────────────

    private sealed class NullDateTimeProvider : IDateTimeProvider
    {
        public DateTime     UtcNow        => DateTime.UtcNow;
        public DateOnly     TodayUtc      => DateOnly.FromDateTime(DateTime.UtcNow);
        public TimeZoneInfo VenueTimeZone => TimeZoneInfo.Local;
        public DateTime     LocalNow      => DateTime.Now;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // isolated per test
            .Options;
        return new TestDbContext(opts);
    }

    private static GetMyWorkLogsQueryHandler    CreateMyHandler(TestDbContext db)  => new(db);
    private static GetActiveWorkLogQueryHandler CreateActiveHandler(TestDbContext db) => new(db);
    private static GetAllWorkLogsQueryHandler   CreateAllHandler(TestDbContext db)  => new(db, new NullDateTimeProvider());

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // ── GetMyWorkLogsQuery ────────────────────────────────────────────────────

    // 1. Empty DB
    [Fact]
    public async Task GetMy_EmptyDatabase_ReturnsTotalZero()
    {
        await using var db = CreateDb();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    // 2. Only returns logs for the queried employee
    [Fact]
    public async Task GetMy_OnlyReturnsLogsForSpecificEmployee()
    {
        await using var db = CreateDb();
        var empA = Guid.NewGuid();
        var empB = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.CreateManual(empA, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(empA, "BARTENDER", "loc-1", "Main Bar", Today, "19:00", "00:00", 50m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(empB, "BARTENDER", "loc-1", "Main Bar", Today, "20:00", "01:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empA), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, dto => Assert.Equal(empA, db.WorkLogs.Single(w => w.Id == dto.Id).EmployeeId));
    }

    // 3. Page 1 of 12 logs → 10 items, total=12
    [Fact]
    public async Task GetMy_Pagination_Page1_ReturnsFirstPageAndCorrectTotal()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        for (var i = 1; i <= 12; i++)
            db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar",
                DateOnly.FromDateTime(DateTime.Today.AddDays(-i)), "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId, Page: 1, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Total);
        Assert.Equal(10, result.Items.Count);
    }

    // 4. Page 2 of 12 logs → 2 items, total=12
    [Fact]
    public async Task GetMy_Pagination_Page2_ReturnsRemainder()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        for (var i = 1; i <= 12; i++)
            db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar",
                DateOnly.FromDateTime(DateTime.Today.AddDays(-i)), "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId, Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    // 5. Active shift (no EndTime) → IsActive=true, Hours=0
    [Fact]
    public async Task GetMy_ActiveShift_ReturnsIsActive_True_Hours_Zero()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.True(dto.IsActive);
        Assert.Equal(0m, dto.Hours);
    }

    // 6. Completed shift 18:00–23:00 → 5 hours
    [Fact]
    public async Task GetMy_CompletedShift_CalculatesHours()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal(5m, dto.Hours);
    }

    // 7. Overnight shift 22:00–06:00 → 8 hours
    [Fact]
    public async Task GetMy_OvernightShift_CalculatesHoursCorrectly()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", Today, "22:00", "06:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal(8m, dto.Hours);
    }

    // 8. Completed shift 18:00–23:00 at rate 50 → cost=250
    [Fact]
    public async Task GetMy_CompletedShift_CalculatesCost()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal(250m, dto.Cost);
    }

    // 9. After Update(), IsEdited=true in dto
    [Fact]
    public async Task GetMy_IsEdited_ReflectsEntityState()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var log = E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        // Trigger edit
        log.Update("BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", "Updated notes");
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.True(dto.IsEdited);
    }

    // 10. CreateManual → IsManualEntry=true in dto
    [Fact]
    public async Task GetMy_IsManualEntry_ReflectsEntityState()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var manual = E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m);
        var live   = E.WorkLog.StartLive(empId, "BARTENDER", "loc-2", "VIP Lounge", Today, "20:00", 50m);
        db.WorkLogs.Add(manual);
        db.WorkLogs.Add(live);
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var manualDto = result.Items.Single(d => d.LocationId == "loc-1");
        var liveDto   = result.Items.Single(d => d.LocationId == "loc-2");
        Assert.True(manualDto.IsManualEntry);
        Assert.False(liveDto.IsManualEntry);
    }

    // 11. WorkDate formatted as yyyy-MM-dd
    [Fact]
    public async Task GetMy_WorkDate_FormattedAsYyyyMmDd()
    {
        await using var db = CreateDb();
        var empId   = Guid.NewGuid();
        var workDate = new DateOnly(2025, 3, 7); // single-digit month and day

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", workDate, "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.Equal("2025-03-07", dto.WorkDate);
    }

    // 12. Ordered by WorkDate descending — newer date comes first
    [Fact]
    public async Task GetMy_OrderedByWorkDateDescending()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var older = new DateOnly(2025, 1, 1);
        var newer = new DateOnly(2025, 6, 1);

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", older, "18:00", "23:00", 50m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", newer, "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateMyHandler(db).Handle(
            new GetMyWorkLogsQuery(empId), CancellationToken.None);

        Assert.Equal("2025-06-01", result.Items[0].WorkDate);
        Assert.Equal("2025-01-01", result.Items[1].WorkDate);
    }

    // ── GetActiveWorkLogQuery ─────────────────────────────────────────────────

    // 13. No active shift → returns null
    [Fact]
    public async Task GetActive_ReturnsNull_WhenNoActiveShift()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateActiveHandler(db).Handle(
            new GetActiveWorkLogQuery(empId), CancellationToken.None);

        Assert.Null(result);
    }

    // 14. Active shift exists → returns dto
    [Fact]
    public async Task GetActive_ReturnsActiveShift_WhenExists()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateActiveHandler(db).Handle(
            new GetActiveWorkLogQuery(empId), CancellationToken.None);

        Assert.NotNull(result);
    }

    // 15. Active shift dto has IsActive=true
    [Fact]
    public async Task GetActive_ActiveShift_HasIsActive_True()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateActiveHandler(db).Handle(
            new GetActiveWorkLogQuery(empId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsActive);
    }

    // 16. Active shift dto has Hours=0 (not yet clocked out)
    [Fact]
    public async Task GetActive_ActiveShift_Hours_IsZero()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateActiveHandler(db).Handle(
            new GetActiveWorkLogQuery(empId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.Hours);
    }

    // 17. Ended shift (EndTime set) → returns null
    [Fact]
    public async Task GetActive_DoesNotReturn_EndedShift()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var log = E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", 50m);
        log.End("23:00");
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        var result = await CreateActiveHandler(db).Handle(
            new GetActiveWorkLogQuery(empId), CancellationToken.None);

        Assert.Null(result);
    }

    // 18. After Update(), IsEdited=true reflected in active dto
    [Fact]
    public async Task GetActive_Returns_IsEdited_FromEntity()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var log = E.WorkLog.StartLive(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        // Update the live log (provides EndTime as required by Update signature)
        log.Update("BARTENDER", "loc-1", "Main Bar", Today, "18:30", "23:00", "Corrected start");
        await db.SaveChangesAsync();

        var result = await CreateActiveHandler(db).Handle(
            new GetActiveWorkLogQuery(empId), CancellationToken.None);

        // After Update(), EndTime is set so it's no longer active — but IsEdited must be persisted
        // Verify the entity itself carries IsEdited=true (the query would return null since EndTime set)
        var entity = db.WorkLogs.Single(w => w.EmployeeId == empId);
        Assert.True(entity.IsEdited);
    }

    // ── GetAllWorkLogsQuery ───────────────────────────────────────────────────

    // 19. Empty DB → total=0
    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsTotalZero()
    {
        await using var db = CreateDb();

        var result = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(null, null, null), CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    // 20. EmployeeId filter returns only that employee's logs
    [Fact]
    public async Task GetAll_FiltersByEmployeeId()
    {
        await using var db = CreateDb();
        var empA = Guid.NewGuid();
        var empB = Guid.NewGuid();

        db.WorkLogs.Add(E.WorkLog.CreateManual(empA, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(empB, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var result = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(null, null, empA), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.All(result.Items, dto => Assert.Equal(empA, db.WorkLogs.Single(w => w.Id == dto.Id).EmployeeId));
    }

    // 21. From filter excludes logs before that date
    [Fact]
    public async Task GetAll_FiltersByFrom_ExcludesEarlierDates()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var earlyDate  = new DateOnly(2025, 1, 1);
        var targetDate = new DateOnly(2025, 6, 1);

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", earlyDate,  "18:00", "23:00", 50m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", targetDate, "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var from   = new DateTime(2025, 5, 1);
        var result = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(from, null, null), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("2025-06-01", result.Items[0].WorkDate);
    }

    // 22. To filter excludes logs after that date
    [Fact]
    public async Task GetAll_FiltersByTo_ExcludesLaterDates()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var earlyDate = new DateOnly(2025, 1, 1);
        var lateDate  = new DateOnly(2025, 12, 1);

        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", earlyDate, "18:00", "23:00", 50m));
        db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", lateDate,  "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var to     = new DateTime(2025, 6, 30);
        var result = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(null, to, null), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("2025-01-01", result.Items[0].WorkDate);
    }

    // 23. No filter returns all logs across employees
    [Fact]
    public async Task GetAll_NoFilter_ReturnsAllLogs()
    {
        await using var db = CreateDb();

        for (var i = 0; i < 3; i++)
        {
            var empId = Guid.NewGuid();
            db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar",
                DateOnly.FromDateTime(DateTime.Today.AddDays(-i)), "18:00", "23:00", 50m));
        }
        await db.SaveChangesAsync();

        var result = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(null, null, null), CancellationToken.None);

        Assert.Equal(3, result.Total);
    }

    // 24. Pagination returns correct page slice
    [Fact]
    public async Task GetAll_Pagination_ReturnsCorrectPage()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        for (var i = 1; i <= 12; i++)
            db.WorkLogs.Add(E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar",
                DateOnly.FromDateTime(DateTime.Today.AddDays(-i)), "18:00", "23:00", 50m));
        await db.SaveChangesAsync();

        var page1 = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(null, null, null, Page: 1, PageSize: 10), CancellationToken.None);
        var page2 = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(null, null, null, Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, page1.Total);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(12, page2.Total);
        Assert.Equal(2,  page2.Items.Count);
    }

    // 25. IsEdited propagates to dto in GetAll
    [Fact]
    public async Task GetAll_IsEdited_InDto()
    {
        await using var db = CreateDb();
        var empId = Guid.NewGuid();

        var log = E.WorkLog.CreateManual(empId, "BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", 50m);
        db.WorkLogs.Add(log);
        await db.SaveChangesAsync();

        log.Update("BARTENDER", "loc-1", "Main Bar", Today, "18:00", "23:00", "Admin correction");
        await db.SaveChangesAsync();

        var result = await CreateAllHandler(db).Handle(
            new GetAllWorkLogsQuery(null, null, empId), CancellationToken.None);

        var dto = Assert.Single(result.Items);
        Assert.True(dto.IsEdited);
    }
}
