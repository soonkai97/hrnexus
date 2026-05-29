using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Announcements.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for Announcement commands (Upsert, Delete, MarkRead).
/// </summary>
public class AnnouncementTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class NullNotificationHub : INotificationHub
    {
        public Task SendToUserAsync(Guid id, NotificationPayload p, CancellationToken ct = default) => Task.CompletedTask;
        public Task BroadcastLiveStatsAsync(LiveStatsDto s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullPushSvc : IPushNotificationService
    {
        public Task SendAsync(Guid id, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendToManyAsync(IEnumerable<Guid> ids, string t, string b, string? u = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullAuditService : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    private static UpsertAnnouncementCommandHandler AnnouncementHandler(TestDbContext db) =>
        new(db, new NullNotificationHub(), new NullPushSvc(), new NullAuditService());

    private static readonly Guid AdminId = Guid.NewGuid();

    // ── UpsertAnnouncementCommand ─────────────────────────────────────────────

    [Fact]
    public async Task UpsertAnnouncement_ThrowsArgument_WhenTitleIsEmpty()
    {
        await using var db = CreateDb();
        var cmd = new UpsertAnnouncementCommand(null, "", "body", "*", null, AdminId, "Admin");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            AnnouncementHandler(db).Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAnnouncement_ThrowsArgument_WhenBodyIsEmpty()
    {
        await using var db = CreateDb();
        var cmd = new UpsertAnnouncementCommand(null, "Title", "  ", "*", null, AdminId, "Admin");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            AnnouncementHandler(db).Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAnnouncement_ThrowsArgument_WhenTitleExceeds200Chars()
    {
        await using var db = CreateDb();
        var longTitle = new string('x', 201);
        var cmd = new UpsertAnnouncementCommand(null, longTitle, "body", "*", null, AdminId, "Admin");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            AnnouncementHandler(db).Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAnnouncement_ThrowsArgument_WhenBodyExceeds5000Chars()
    {
        await using var db = CreateDb();
        var longBody = new string('x', 5001);
        var cmd = new UpsertAnnouncementCommand(null, "Title", longBody, "*", null, AdminId, "Admin");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            AnnouncementHandler(db).Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAnnouncement_ThrowsArgument_WhenExpiryInPast()
    {
        await using var db = CreateDb();
        var pastDate = DateTime.UtcNow.AddDays(-1);
        var cmd = new UpsertAnnouncementCommand(null, "Title", "body", "*", pastDate, AdminId, "Admin");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            AnnouncementHandler(db).Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAnnouncement_ThrowsArgument_WhenRoleDoesNotExist()
    {
        await using var db = CreateDb();
        // "BARTENDER" not seeded into Roles table
        var cmd = new UpsertAnnouncementCommand(null, "Title", "body", "BARTENDER", null, AdminId, "Admin");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            AnnouncementHandler(db).Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAnnouncement_Create_Succeeds_WithWildcardRole()
    {
        await using var db = CreateDb();
        var cmd = new UpsertAnnouncementCommand(null, "Title", "body", "*",
            null, AdminId, "Admin");

        var id = await AnnouncementHandler(db).Handle(cmd, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        var created = await db.Announcements.FirstAsync(a => a.Id == id);
        Assert.Equal("Title", created.Title);
        Assert.Equal("*", created.TargetRoles);
    }

    [Fact]
    public async Task UpsertAnnouncement_Create_Succeeds_WithFutureExpiry()
    {
        await using var db = CreateDb();
        var cmd = new UpsertAnnouncementCommand(null, "Staff Notice", "Details here", "*",
            DateTime.UtcNow.AddDays(7), AdminId, "Admin");

        var id = await AnnouncementHandler(db).Handle(cmd, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        var created = await db.Announcements.FirstAsync(a => a.Id == id);
        Assert.NotNull(created.ExpiresAt);
    }

    [Fact]
    public async Task UpsertAnnouncement_Update_Succeeds()
    {
        await using var db = CreateDb();
        var ann = E.Announcement.Create("Old Title", "Old body", "*", AdminId, "Admin");
        db.Announcements.Add(ann);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var cmd = new UpsertAnnouncementCommand(ann.Id, "New Title", "New body", "*",
            null, AdminId, "Admin");

        await AnnouncementHandler(db).Handle(cmd, CancellationToken.None);

        var updated = await db.Announcements.FirstAsync(a => a.Id == ann.Id);
        Assert.Equal("New Title", updated.Title);
        Assert.Equal("New body", updated.Body);
    }

    [Fact]
    public async Task UpsertAnnouncement_Update_ThrowsKeyNotFound_WhenAnnouncementNotFound()
    {
        await using var db = CreateDb();
        var cmd = new UpsertAnnouncementCommand(Guid.NewGuid(), "Title", "body", "*",
            null, AdminId, "Admin");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            AnnouncementHandler(db).Handle(cmd, CancellationToken.None));
    }

    // ── DeleteAnnouncementCommand ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteAnnouncement_ThrowsKeyNotFound_WhenNotFound()
    {
        await using var db = CreateDb();
        var handler = new DeleteAnnouncementCommandHandler(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteAnnouncementCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAnnouncement_Succeeds_RemovesAnnouncement()
    {
        await using var db = CreateDb();
        var ann = E.Announcement.Create("Title", "body", "*", AdminId, "Admin");
        db.Announcements.Add(ann);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new DeleteAnnouncementCommandHandler(db);
        await handler.Handle(new DeleteAnnouncementCommand(ann.Id), CancellationToken.None);

        Assert.Equal(0, await db.Announcements.CountAsync());
    }

    // ── MarkAnnouncementReadCommand ───────────────────────────────────────────

    [Fact]
    public async Task MarkAnnouncementRead_CreatesReadReceipt()
    {
        await using var db = CreateDb();
        var annId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        var handler = new MarkAnnouncementReadCommandHandler(db);
        await handler.Handle(new MarkAnnouncementReadCommand(annId, empId), CancellationToken.None);

        Assert.Equal(1, await db.AnnouncementReads.CountAsync());
    }

    [Fact]
    public async Task MarkAnnouncementRead_IsIdempotent_DoesNotDuplicateRead()
    {
        await using var db = CreateDb();
        var annId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        var handler = new MarkAnnouncementReadCommandHandler(db);
        await handler.Handle(new MarkAnnouncementReadCommand(annId, empId), CancellationToken.None);
        await handler.Handle(new MarkAnnouncementReadCommand(annId, empId), CancellationToken.None);

        // Should still be exactly 1 read receipt
        Assert.Equal(1, await db.AnnouncementReads.CountAsync());
    }

}
