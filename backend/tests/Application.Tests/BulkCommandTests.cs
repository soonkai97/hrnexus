using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for the bulk admin commands: assign locations/roles (additive),
/// deactivate (with the whole-selection last-admin guard), and mark-leave-no-show.
/// </summary>
public class BulkCommandTests
{
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string et, string ei, string a, Guid? pid, string pn,
            string? c = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static E.Staff MakeStaff(string staffId, IEnumerable<string>? roles = null,
        IEnumerable<string>? locations = null) =>
        E.Staff.Create(staffId, "Test", "User", $"{staffId}@t.com", "hash",
            (roles ?? new[] { "BARTENDER" }).ToList(),
            locations: locations?.ToList());

    // ── BulkAssignLocations ──────────────────────────────────────────────────────

    [Fact]
    public async Task BulkAssignLocations_AddsLocationsToEverySelectedStaff()
    {
        await using var db = CreateDb();
        var loc1 = E.Location.Create("Main Bar");
        var loc2 = E.Location.Create("Rooftop");
        db.Locations.AddRange(loc1, loc2);
        var a = MakeStaff("A");
        var b = MakeStaff("B");
        db.Staff.AddRange(a, b);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var n = await new BulkAssignLocationsCommandHandler(db, new NullAudit()).Handle(
            new BulkAssignLocationsCommand([a.Id, b.Id], [loc1.Id, loc2.Id]), CancellationToken.None);

        Assert.Equal(2, n);
        Assert.Equal(2, await db.StaffLocations.CountAsync(sl => sl.EmployeeId == a.Id));
        Assert.Equal(2, await db.StaffLocations.CountAsync(sl => sl.EmployeeId == b.Id));
    }

    [Fact]
    public async Task BulkAssignLocations_IsAdditive_DoesNotDuplicateExisting()
    {
        await using var db = CreateDb();
        var loc1 = E.Location.Create("Main Bar");
        var loc2 = E.Location.Create("Rooftop");
        db.Locations.AddRange(loc1, loc2);
        var a = MakeStaff("A", locations: new[] { loc1.Id });   // already has loc1
        db.Staff.Add(a);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new BulkAssignLocationsCommandHandler(db, new NullAudit()).Handle(
            new BulkAssignLocationsCommand([a.Id], [loc1.Id, loc2.Id]), CancellationToken.None);

        // loc1 not duplicated; loc2 added → exactly 2 rows.
        Assert.Equal(2, await db.StaffLocations.CountAsync(sl => sl.EmployeeId == a.Id));
    }

    [Fact]
    public async Task BulkAssignLocations_ThrowsArgument_WhenLocationDoesNotExist()
    {
        await using var db = CreateDb();
        var a = MakeStaff("A");
        db.Staff.Add(a);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new BulkAssignLocationsCommandHandler(db, new NullAudit()).Handle(
                new BulkAssignLocationsCommand([a.Id], ["phantom-location"]), CancellationToken.None));
    }

    [Fact]
    public async Task BulkAssignLocations_ReturnsZero_WhenNoStaffSelected()
    {
        await using var db = CreateDb();
        var n = await new BulkAssignLocationsCommandHandler(db, new NullAudit()).Handle(
            new BulkAssignLocationsCommand([], ["x"]), CancellationToken.None);
        Assert.Equal(0, n);
    }

    // ── BulkAssignRoles ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkAssignRoles_AddsRoleAdditively_WithoutDroppingExisting()
    {
        await using var db = CreateDb();
        db.Roles.AddRange(E.Role.Create("bartender"), E.Role.Create("waitress"));
        var a = MakeStaff("A", roles: new[] { "BARTENDER" });
        db.Staff.Add(a);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new BulkAssignRolesCommandHandler(db, new NullAudit()).Handle(
            new BulkAssignRolesCommand([a.Id], ["WAITRESS"]), CancellationToken.None);

        var roles = await db.StaffRoles.Where(r => r.EmployeeId == a.Id).Select(r => r.Role).ToListAsync();
        Assert.Equal(2, roles.Count);
        Assert.Contains("BARTENDER", roles);
        Assert.Contains("WAITRESS", roles);
    }

    [Fact]
    public async Task BulkAssignRoles_ThrowsArgument_WhenRoleDoesNotExist()
    {
        await using var db = CreateDb();
        db.Roles.Add(E.Role.Create("bartender"));
        var a = MakeStaff("A");
        db.Staff.Add(a);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new BulkAssignRolesCommandHandler(db, new NullAudit()).Handle(
                new BulkAssignRolesCommand([a.Id], ["GHOST"]), CancellationToken.None));
    }

    // ── BulkDeactivate ───────────────────────────────────────────────────────────

    /// <summary>Seeds a role that grants admin-portal access and N staff holding it.</summary>
    private static async Task<List<E.Staff>> SeedAdminsAsync(TestDbContext db, int count)
    {
        db.RolePermissions.Add(E.RolePermission.Create("MANAGER", AppConstants.Permissions.PortalAdmin));
        var admins = new List<E.Staff>();
        for (var i = 0; i < count; i++)
        {
            var s = MakeStaff($"ADM{i}", roles: new[] { "MANAGER" });
            db.Staff.Add(s);
            admins.Add(s);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return admins;
    }

    [Fact]
    public async Task BulkDeactivate_DeactivatesNonAdminStaff()
    {
        await using var db = CreateDb();
        var a = MakeStaff("A", roles: new[] { "EMPLOYEE" });
        var b = MakeStaff("B", roles: new[] { "EMPLOYEE" });
        db.Staff.AddRange(a, b);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var n = await new BulkDeactivateStaffCommandHandler(db, new NullAudit()).Handle(
            new BulkDeactivateStaffCommand([a.Id, b.Id]), CancellationToken.None);

        Assert.Equal(2, n);
        Assert.False((await db.Staff.FirstAsync(s => s.Id == a.Id)).IsActive);
        Assert.False((await db.Staff.FirstAsync(s => s.Id == b.Id)).IsActive);
    }

    [Fact]
    public async Task BulkDeactivate_Throws_WhenSelectionWouldRemoveEveryActiveAdmin()
    {
        await using var db = CreateDb();
        var admins = await SeedAdminsAsync(db, 2);   // exactly two admins exist

        // Selecting BOTH admins would leave zero — must be refused.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new BulkDeactivateStaffCommandHandler(db, new NullAudit()).Handle(
                new BulkDeactivateStaffCommand(admins.Select(a => a.Id).ToList()), CancellationToken.None));

        Assert.Equal(2, await db.Staff.CountAsync(s => s.IsActive));   // untouched
    }

    [Fact]
    public async Task BulkDeactivate_Succeeds_WhenAtLeastOneAdminRemains()
    {
        await using var db = CreateDb();
        var admins = await SeedAdminsAsync(db, 2);

        // Deactivating only ONE of the two admins leaves one active → allowed.
        var n = await new BulkDeactivateStaffCommandHandler(db, new NullAudit()).Handle(
            new BulkDeactivateStaffCommand([admins[0].Id]), CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Equal(1, await db.Staff.CountAsync(s => s.IsActive));
    }

    // ── BulkMarkLeaveNoShow ──────────────────────────────────────────────────────

    [Fact]
    public async Task BulkMarkNoShow_ChangesTypeToNoShow_AndSkipsAlreadyNoShow()
    {
        await using var db = CreateDb();
        var staff = MakeStaff("A");
        db.Staff.Add(staff);
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var annual  = E.LeaveRequest.Create(staff.Id, "annual", start, start, null, null, null, null);
        var already = E.LeaveRequest.Create(staff.Id, AppConstants.Leave.NoShow, start, start, null, null, null, null);
        db.LeaveRequests.AddRange(annual, already);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var n = await new BulkMarkLeaveNoShowCommandHandler(db, new NullAudit()).Handle(
            new BulkMarkLeaveNoShowCommand([annual.Id, already.Id]), CancellationToken.None);

        Assert.Equal(1, n);   // only the annual one changed; the no-show one was skipped
        Assert.Equal(AppConstants.Leave.NoShow, (await db.LeaveRequests.FirstAsync(l => l.Id == annual.Id)).LeaveType);
    }

    [Fact]
    public async Task BulkMarkNoShow_ReturnsZero_WhenNoIds()
    {
        await using var db = CreateDb();
        var n = await new BulkMarkLeaveNoShowCommandHandler(db, new NullAudit()).Handle(
            new BulkMarkLeaveNoShowCommand([]), CancellationToken.None);
        Assert.Equal(0, n);
    }
}
