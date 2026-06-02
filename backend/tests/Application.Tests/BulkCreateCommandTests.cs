using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Locations.Commands;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for the bulk-CREATE (import) commands: locations, roles, staff (partial
/// success + generated passwords), and venue-wide leave (delegates to UpsertLeaveCommand).
/// </summary>
public class BulkCreateCommandTests
{
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn,
            string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string p) => "hash:" + p;
        public bool Verify(string p, string h) => h == "hash:" + p;
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // ── Locations ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkCreateLocations_CreatesNew_SkipsDuplicatesAndExisting()
    {
        await using var db = CreateDb();
        db.Locations.Add(E.Location.Create("Main Bar"));   // stored as "MAIN BAR"
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new BulkCreateLocationsCommandHandler(db, new NullAudit()).Handle(
            new BulkCreateLocationsCommand(["Main Bar", "Rooftop", "rooftop", "Lounge"]),
            CancellationToken.None);

        // "Main Bar" exists; "Rooftop"/"rooftop" dedupe to one new; "Lounge" new → 2 created, 1 skipped (existing).
        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(3, await db.Locations.CountAsync());
    }

    // ── Delete location ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteLocation_ThrowsKeyNotFound_WhenMissing()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new DeleteLocationCommandHandler(db, new NullAudit())
                .Handle(new DeleteLocationCommand("ghost"), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteLocation_SoftDeletes_WhenNoActiveShift()
    {
        await using var db = CreateDb();
        var loc = E.Location.Create("Main Bar");
        db.Locations.Add(loc);
        // A *closed* shift at the location must not block deletion.
        db.WorkLogs.Add(E.WorkLog.CreateManual(Guid.NewGuid(), "BARTENDER", loc.Id, "Main Bar",
            new DateOnly(2026, 6, 1), "18:00", "23:00", 50m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new DeleteLocationCommandHandler(db, new NullAudit())
            .Handle(new DeleteLocationCommand(loc.Id), CancellationToken.None);

        // Soft-deleted (IsDeleted flag set). The real AppDbContext's global query filter then hides it;
        // the InMemory test double doesn't apply that filter, so assert the flag directly.
        var stored = await db.Locations.FirstAsync(l => l.Id == loc.Id);
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task DeleteLocation_ThrowsInvalidOperation_WhenShiftStillInProgress()
    {
        // A location someone is actively clocked into must not be deletable mid-shift.
        await using var db = CreateDb();
        var loc = E.Location.Create("Main Bar");
        db.Locations.Add(loc);
        db.WorkLogs.Add(E.WorkLog.StartLive(Guid.NewGuid(), "BARTENDER", loc.Id, "Main Bar",
            new DateOnly(2026, 6, 1), "18:00", 50m));   // EndTime == null → active
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeleteLocationCommandHandler(db, new NullAudit())
                .Handle(new DeleteLocationCommand(loc.Id), CancellationToken.None));

        Assert.Single(await db.Locations.ToListAsync());   // still there — not deleted
    }

    // ── Roles ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkCreateRoles_CreatesNewRolesWithRateHistory()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("BARTENDER"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new BulkCreateRolesCommandHandler(db, new NullAudit()).Handle(
            new BulkCreateRolesCommand([new("bartender", 20m), new("Waitress", 18m), new("Doorman", 22m)]),
            CancellationToken.None);

        Assert.Equal(2, result.Created);   // bartender skipped (exists); waitress + doorman new
        Assert.Equal(1, result.Skipped);
        Assert.NotNull(await db.Roles.FindAsync("WAITRESS"));
        Assert.Equal(2, await db.RoleRates.CountAsync());   // one baseline per new role
    }

    // ── Staff (partial success) ──────────────────────────────────────────────────

    [Fact]
    public async Task BulkRegisterStaff_ImportsValidRows_AndReportsBadOnes()
    {
        await using var db = CreateDb();
        db.Staff.Add(E.Staff.Create("EXISTING", "Al", "Ready", "taken@t.com", "hash",
            new List<string> { "STAFF" }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = new List<BulkStaffRow>
        {
            new("NEW1", "Alice", "Tan", "alice@t.com", new List<string> { "STAFF" }, null, "Passw0rd!"),  // ok
            new("EXISTING", "Dup", null, null, new List<string> { "STAFF" }, null, "Passw0rd!"),          // dup id
            new("NOROLE", "Bob", null, null, new List<string>(), null, "Passw0rd!"),                       // no role
            new("NEW2", "", null, null, new List<string> { "STAFF" }, null, "Passw0rd!"),                  // no first name
            new("NOPWD", "Cara", null, null, new List<string> { "STAFF" }),                                // no password
        };

        var result = await new BulkRegisterStaffCommandHandler(db, new StubHasher(), new NullAudit())
            .Handle(new BulkRegisterStaffCommand(rows), CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(4, result.Failed);
        var ok = result.Rows.Single(r => r.Success);
        Assert.Equal("NEW1", ok.StaffId);
        Assert.Contains(result.Rows, r => r.StaffId == "NOPWD" && !r.Success && r.Error!.Contains("Password"));
        Assert.Equal(2, await db.Staff.CountAsync());               // existing + NEW1
    }

    [Fact]
    public async Task BulkRegisterStaff_RejectsDuplicateStaffIdsWithinTheBatch()
    {
        await using var db = CreateDb();
        var rows = new List<BulkStaffRow>
        {
            new("DUP", "First", null, null, new List<string> { "STAFF" }, null, "Passw0rd!"),
            new("dup", "Second", null, null, new List<string> { "STAFF" }, null, "Passw0rd!"),   // same id, different case
        };

        var result = await new BulkRegisterStaffCommandHandler(db, new StubHasher(), new NullAudit())
            .Handle(new BulkRegisterStaffCommand(rows), CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Failed);
    }

    // ── Leave (venue-wide, delegates to UpsertLeaveCommand) ──────────────────────

    /// <summary>Minimal ISender that returns a fresh Guid for each UpsertLeaveCommand — except for
    /// a designated employee, for whom it throws (to exercise the partial-success path).</summary>
    private sealed class StubSender(Guid failFor) : ISender
    {
        public int Calls { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Calls++;
            var cmd = (UpsertLeaveCommand)(object)request!;
            if (cmd.EmployeeId == failFor) throw new InvalidOperationException("Insufficient balance.");
            return Task.FromResult((TResponse)(object)Guid.NewGuid());
        }
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task BulkCreateLeave_CreatesPerEmployee_AndReportsPerEmployeeFailures()
    {
        var ok1 = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var ok2 = Guid.NewGuid();
        var sender = new StubSender(failFor: bad);

        var result = await new BulkCreateLeaveCommandHandler(sender).Handle(
            new BulkCreateLeaveCommand(
                [ok1, bad, ok2], "unpaid",
                new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
                null, null, null, "Public holiday", "approved"),
            CancellationToken.None);

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Contains(bad.ToString(), result.Errors[0]);
        Assert.Equal(3, sender.Calls);
    }
}
