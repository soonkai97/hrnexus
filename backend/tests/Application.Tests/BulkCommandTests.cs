using HRNexus.Application.Admin.Commands;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Handler-level tests for the bulk admin commands: assign roles (additive),
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

    private static E.Staff MakeStaff(string staffId, IEnumerable<Guid>? roles = null) =>
        E.Staff.Create(staffId, "Test", "User", $"{staffId}@t.com", "hash",
            (roles ?? Enumerable.Empty<Guid>()).ToList());

    // ── BulkAssignRoles ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkAssignRoles_AddsRoleAdditively_WithoutDroppingExisting()
    {
        await using var db = CreateDb();
        var roleIds = await RoleSeed.EnsureRolesAsync(db, "BARTENDER", "WAITRESS");
        var a = MakeStaff("A", roles: new[] { roleIds["BARTENDER"] });
        db.Staff.Add(a);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new BulkAssignRolesCommandHandler(db, new NullAudit()).Handle(
            new BulkAssignRolesCommand([a.Id], [roleIds["WAITRESS"]]), CancellationToken.None);

        // r.Role is the Role navigation; its Name is the uppercase-normalised role name.
        var roles = await db.StaffRoles.Where(r => r.EmployeeId == a.Id).Select(r => r.Role.Name).ToListAsync();
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
                new BulkAssignRolesCommand([a.Id], [Guid.NewGuid()]), CancellationToken.None));
    }

    // ── BulkDeactivate ───────────────────────────────────────────────────────────

    /// <summary>Seeds a role that grants role-management access and N staff holding it.</summary>
    private static async Task<List<E.Staff>> SeedAdminsAsync(TestDbContext db, int count)
    {
        var managerId = await RoleSeed.EnsureRoleAsync(db, "FRONT DESK");
        db.RolePermissions.Add(E.RolePermission.Create(managerId, AppConstants.Permissions.RolesManage));
        var admins = new List<E.Staff>();
        for (var i = 0; i < count; i++)
        {
            var s = MakeStaff($"ADM{i}", roles: new[] { managerId });
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
        var a = MakeStaff("A");
        var b = MakeStaff("B");
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
