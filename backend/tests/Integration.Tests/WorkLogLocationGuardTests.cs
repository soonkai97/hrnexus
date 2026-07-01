using HRNexus.Application.WorkLog.Commands;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Proves the staff↔location assignment guard works against REAL PostgreSQL — the
/// <c>StaffLocations</c> join table + <c>Staff.LocationEntries</c> navigation that gate where a
/// shift may be logged. EF Core InMemory cannot faithfully model the composite-key join: a
/// manually-set shadow FK silently fails to link the row to its parent, so the guard query
/// (<c>s.LocationEntries.Any(l =&gt; l.LocationId == req.LocationId)</c>) can only be trusted once
/// exercised end-to-end on a database that enforces the relationship for real.
///
/// Uses <see cref="CreateManualWorkLogCommandHandler"/> because it depends only on the DbContext
/// (no SignalR sender), and it runs the identical location-guard query as the live clock-in path.
/// </summary>
[Collection("postgres")]
public class WorkLogLocationGuardTests(PostgresFixture fx)
{
    /// <summary>Seeds a role, a location, and a staff member — optionally assigned to that location.</summary>
    private async Task<(Guid empId, Guid roleId, string locId)> SeedAsync(bool assignLocation)
    {
        await using var db = fx.NewDb();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "BARTENDER");
        if (role is null)
        {
            role = E.Role.Create("bartender", hourlyRate: 20m);
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var loc = E.Location.Create($"Venue-{Guid.NewGuid():N}");
        db.Locations.Add(loc);

        var suffix = Guid.NewGuid().ToString("N");
        var staff = E.Staff.Create(
            staffId: $"LOC{suffix}"[..12], firstName: "Loc", lastName: "Guard",
            email: $"{suffix}@t.com", passwordHash: "hash",
            roles: new List<Guid> { role.Id },
            locations: assignLocation ? new List<string> { loc.Id } : null);
        db.Staff.Add(staff);

        await db.SaveChangesAsync();
        return (staff.Id, role.Id, loc.Id);
    }

    [Fact]
    public async Task ManualWorkLog_Rejected_WhenStaffNotAssignedToLocation()
    {
        var (empId, roleId, locId) = await SeedAsync(assignLocation: false);

        await using var db = fx.NewDb();
        var handler = new CreateManualWorkLogCommandHandler(db);
        var cmd = new CreateManualWorkLogCommand(
            empId, roleId, locId, new DateOnly(2026, 3, 1), "09:00", "17:00", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(cmd, CancellationToken.None));
        Assert.Contains("not assigned to this location", ex.Message);

        // The guard must reject BEFORE persisting — no orphaned shift row.
        await using var verify = fx.NewDb();
        Assert.Equal(0, await verify.WorkLogs.CountAsync(w => w.EmployeeId == empId));
    }

    [Fact]
    public async Task ManualWorkLog_Succeeds_WhenStaffAssignedToLocation()
    {
        var (empId, roleId, locId) = await SeedAsync(assignLocation: true);

        await using var db = fx.NewDb();
        var handler = new CreateManualWorkLogCommandHandler(db);
        var cmd = new CreateManualWorkLogCommand(
            empId, roleId, locId, new DateOnly(2026, 3, 1), "09:00", "17:00", null);

        var id = await handler.Handle(cmd, CancellationToken.None);

        await using var verify = fx.NewDb();
        var log = await verify.WorkLogs.FirstOrDefaultAsync(w => w.Id == id);
        Assert.NotNull(log);
        Assert.Equal(empId, log!.EmployeeId);
        Assert.Equal(locId, log.LocationId);
    }
}
