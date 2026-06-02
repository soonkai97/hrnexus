using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Infrastructure.Services;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// AuditService attributes an entry to the authenticated caller when a command doesn't supply a
/// performer — so the trail records WHICH admin acted instead of a placeholder like "Admin"/null.
/// </summary>
public class AuditServiceTests
{
    private sealed class StubCurrentUser(Guid? id, string? name) : ICurrentUserService
    {
        public Guid?   UserId   => id;
        public string? UserName => name;
    }

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    [Fact]
    public async Task LogAsync_AttributesToTheAuthenticatedCaller_WhenPerformerNotSupplied()
    {
        await using var db = CreateDb();
        var caller = Guid.NewGuid();
        var svc = new AuditService(db, new StubCurrentUser(caller, "HRADMIN"));

        // Mirrors the admin/bulk commands that historically passed null + "Admin".
        await svc.LogAsync("Staff", "s1", "Created", performedById: null, performedByName: "Admin");

        var row = await db.AuditLogs.SingleAsync();
        Assert.Equal(caller, row.PerformedById);
        Assert.Equal("HRADMIN", row.PerformedByName);   // real caller, not the "Admin" placeholder
    }

    [Fact]
    public async Task LogAsync_KeepsAnExplicitPerformer_WhenSupplied()
    {
        await using var db = CreateDb();
        var explicitActor = Guid.NewGuid();
        // currentUser is someone else — the explicitly-passed performer must win.
        var svc = new AuditService(db, new StubCurrentUser(Guid.NewGuid(), "SOMEONE_ELSE"));

        await svc.LogAsync("WorkLog", "w1", "Updated", performedById: explicitActor, performedByName: "Jane Manager");

        var row = await db.AuditLogs.SingleAsync();
        Assert.Equal(explicitActor, row.PerformedById);
        Assert.Equal("Jane Manager", row.PerformedByName);
    }

    [Fact]
    public async Task LogAsync_FallsBackToProvidedName_WhenNoCallerContext()
    {
        // e.g. a background job with no HttpContext: id stays null, the provided label is kept.
        await using var db = CreateDb();
        var svc = new AuditService(db, new StubCurrentUser(null, null));

        await svc.LogAsync("Staff", "s1", "Created", performedById: null, performedByName: "System");

        var row = await db.AuditLogs.SingleAsync();
        Assert.Null(row.PerformedById);
        Assert.Equal("System", row.PerformedByName);
    }
}
