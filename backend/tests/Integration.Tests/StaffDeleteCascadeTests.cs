using HRNexus.Application.Staff.Commands;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Proves that deleting a staff member removes ALL their related data — over real PostgreSQL,
/// because delete is a SOFT-delete (IsDeleted=true) which does NOT trigger DB cascade, so the
/// command must remove the join rows (assigned jobs) and other related records itself.
/// EF InMemory can't faithfully model the StaffRole composite-key join, so this lives
/// in the integration suite.
/// </summary>
[Collection("postgres")]
public class StaffDeleteCascadeTests(PostgresFixture fx)
{
    [Fact]
    public async Task Delete_RemovesAssignedJobsAndLeave()
    {
        Guid empId;
        await using (var db = fx.NewDb())
        {
            var role = E.Role.Create($"JOB{Guid.NewGuid():N}"[..12], hourlyRate: 20m);
            db.Roles.Add(role);
            var staff = E.Staff.Create($"DC{Guid.NewGuid():N}"[..10], "Del", "Cascade", null, "hash",
                new List<Guid> { role.Id });
            db.Staff.Add(staff);
            await db.SaveChangesAsync();

            db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, "annual",
                DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, null, null, null));
            await db.SaveChangesAsync();
            empId = staff.Id;
        }

        await using (var db = fx.NewDb())
        {
            var handler = new DeleteStaffCommandHandler(db);
            await handler.Handle(new DeleteStaffCommand(empId), CancellationToken.None);
        }

        // The staff is soft-deleted (hidden) and every related row is gone.
        await using (var verify = fx.NewDb())
        {
            Assert.Empty(await verify.Staff.Where(s => s.Id == empId).ToListAsync());          // filtered = hidden
            Assert.Empty(await verify.StaffRoles.Where(r => r.EmployeeId == empId).ToListAsync());       // jobs
            Assert.Empty(await verify.LeaveRequests.Where(l => l.EmployeeId == empId).ToListAsync());    // leave
        }
    }
}
