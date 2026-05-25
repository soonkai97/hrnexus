using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Announcements.Queries;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Tests for GetAllAnnouncementsQueryHandler — verifies pagination and read-receipt counting.
/// Why these matters: the admin table depends on correct page slices and accurate read counts
/// to gauge staff engagement; wrong counts would mislead admins about reach.
/// </summary>
public class GetAllAnnouncementsQueryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // isolated per test
            .Options;
        return new TestDbContext(opts);
    }

    private static GetAllAnnouncementsQueryHandler CreateHandler(TestDbContext db) => new(db);

    private static Announcement MakeAnnouncement(string title = "Test Notice") =>
        Announcement.Create(title, "Body text.", "*", Guid.NewGuid(), "Admin");

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_EmptyDatabase_ReturnsTotalZero()
    {
        await using var db = CreateDb();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(), CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_Page1_ReturnsFirstPageAndCorrectTotal()
    {
        await using var db = CreateDb();
        for (var i = 1; i <= 12; i++)
            db.Announcements.Add(MakeAnnouncement($"Notice {i}"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(Page: 1, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Total);
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public async Task Handle_Page2_ReturnsRemainingItems()
    {
        await using var db = CreateDb();
        for (var i = 1; i <= 12; i++)
            db.Announcements.Add(MakeAnnouncement($"Notice {i}"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Handle_PageBeyondData_ReturnsEmptyItems()
    {
        await using var db = CreateDb();
        db.Announcements.Add(MakeAnnouncement());
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(Page: 99, PageSize: 10), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Empty(result.Items);
    }

    // ── ReadCount ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AnnouncementWithNoReads_ReadCountIsZero()
    {
        await using var db = CreateDb();
        db.Announcements.Add(MakeAnnouncement());
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(), CancellationToken.None);

        // No AnnouncementRead rows → ReadCount must be 0 (not null, not error)
        Assert.Equal(0, result.Items[0].ReadCount);
    }

    [Fact]
    public async Task Handle_AnnouncementWithReads_ReadCountMatchesReceipts()
    {
        await using var db = CreateDb();
        var announcement = MakeAnnouncement("Popular Notice");
        db.Announcements.Add(announcement);
        // Three staff members have read it
        db.AnnouncementReads.Add(AnnouncementRead.Create(announcement.Id, Guid.NewGuid()));
        db.AnnouncementReads.Add(AnnouncementRead.Create(announcement.Id, Guid.NewGuid()));
        db.AnnouncementReads.Add(AnnouncementRead.Create(announcement.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(), CancellationToken.None);

        Assert.Equal(3, result.Items[0].ReadCount);
    }

    [Fact]
    public async Task Handle_MultipleAnnouncements_ReadCountsAreIndependent()
    {
        await using var db = CreateDb();
        var a1 = MakeAnnouncement("First");
        var a2 = MakeAnnouncement("Second");
        db.Announcements.Add(a1);
        db.Announcements.Add(a2);
        // Two reads on a1, zero on a2
        db.AnnouncementReads.Add(AnnouncementRead.Create(a1.Id, Guid.NewGuid()));
        db.AnnouncementReads.Add(AnnouncementRead.Create(a1.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(), CancellationToken.None);

        var dto1 = result.Items.First(i => i.Title == "First");
        var dto2 = result.Items.First(i => i.Title == "Second");
        Assert.Equal(2, dto1.ReadCount);
        Assert.Equal(0, dto2.ReadCount);
    }

    // ── DTO field mapping ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DtoFields_MappedFromEntity()
    {
        await using var db = CreateDb();
        var createdById   = Guid.NewGuid();
        var announcement  = Announcement.Create(
            "Staff Meeting", "All hands at 9 pm.", "manager,bartender",
            createdById, "Jane Admin", expiresAt: null);
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetAllAnnouncementsQuery(), CancellationToken.None);

        var dto = result.Items[0];
        Assert.Equal("Staff Meeting",         dto.Title);
        Assert.Equal("All hands at 9 pm.",    dto.Body);
        Assert.Equal("manager,bartender",      dto.TargetRoles);
        Assert.Equal("Jane Admin",             dto.CreatedByName);
        Assert.True(dto.IsActive);
        Assert.Null(dto.ExpiresAt);
    }
}
