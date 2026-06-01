using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using HRNexus.Application.Common.Interfaces;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests.Infrastructure;

/// <summary>
/// Lightweight in-memory DbContext used only in unit tests.
/// Applies the minimal configuration needed by the InMemory provider
/// (mainly marking keyless / composite-key entities).
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<E.Staff>            Staff             => Set<E.Staff>();
    public DbSet<E.StaffRole>        StaffRoles        => Set<E.StaffRole>();
    public DbSet<E.Location>         Locations         => Set<E.Location>();
    public DbSet<E.Role>             Roles             => Set<E.Role>();
    public DbSet<E.RoleRate>         RoleRates         => Set<E.RoleRate>();
    public DbSet<E.RolePermission>          RolePermissions          => Set<E.RolePermission>();
    public DbSet<E.StaffPermissionOverride> StaffPermissionOverrides => Set<E.StaffPermissionOverride>();
    public DbSet<E.WorkLog>          WorkLogs          => Set<E.WorkLog>();
    public DbSet<E.LeaveRequest>     LeaveRequests     => Set<E.LeaveRequest>();
    public DbSet<E.LeaveEntitlement> LeaveEntitlements => Set<E.LeaveEntitlement>();
    public DbSet<E.Notification>     Notifications     => Set<E.Notification>();
    public DbSet<E.AuditLog>         AuditLogs         => Set<E.AuditLog>();
    public DbSet<E.PushSubscription> PushSubscriptions => Set<E.PushSubscription>();
    public DbSet<E.PayrollLock>          PayrollLocks           => Set<E.PayrollLock>();

    // InMemory provider does not support transactions — return null so handlers skip tx logic.
    public Task<IDbContextTransaction?> BeginRepeatableReadAsync(CancellationToken ct = default)
        => Task.FromResult<IDbContextTransaction?>(null);

    public Task<IDbContextTransaction?> BeginSerializableAsync(CancellationToken ct = default)
        => Task.FromResult<IDbContextTransaction?>(null);

    // InMemory uses a non-retrying strategy — execute the operation directly.
    public Task<T> ExecuteInStrategyAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        => operation(ct);

    public void ClearChangeTracker() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // StaffRole: composite PK + auto-include so Staff.RoleEntries is always loaded,
        // matching AppDbContext behaviour (SetRoles relies on the collection being populated).
        mb.Entity<E.StaffRole>()
            .HasKey(r => new { r.EmployeeId, r.Role });
        mb.Entity<E.Staff>()
            .Navigation(s => s.RoleEntries)
            .AutoInclude();

        // Role: string PK on "Name" property (not "Id")
        mb.Entity<E.Role>()
            .HasKey(r => r.Name);

        // Permission tables: composite PKs (no auto-discovered Id)
        mb.Entity<E.RolePermission>()
            .HasKey(r => new { r.RoleName, r.PermissionKey });
        mb.Entity<E.StaffPermissionOverride>()
            .HasKey(o => new { o.EmployeeId, o.PermissionKey });
    }
}
