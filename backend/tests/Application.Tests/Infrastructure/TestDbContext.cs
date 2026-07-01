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
    public DbSet<E.StaffLocation>    StaffLocations    => Set<E.StaffLocation>();
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
    public DbSet<E.CashAdvance>          CashAdvances           => Set<E.CashAdvance>();

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
        // Role: GUID PK on "Id" (Name is now a normal unique display column).
        mb.Entity<E.Role>()
            .HasKey(r => r.Id);

        // StaffRole: composite PK (EmployeeId, RoleId). The Role navigation is AutoInclude()d so
        // name-based auth (staffRole.Role.Name) keeps working, matching AppDbContext. The Staff→
        // StaffRole relationship is keyed on EmployeeId (a second FK on the same entity).
        mb.Entity<E.StaffRole>()
            .HasKey(r => new { r.EmployeeId, r.RoleId });
        mb.Entity<E.StaffRole>()
            .HasOne(r => r.Role).WithMany().HasForeignKey(r => r.RoleId);
        mb.Entity<E.StaffRole>()
            .Navigation(r => r.Role).AutoInclude();
        mb.Entity<E.Staff>()
            .HasMany(s => s.RoleEntries).WithOne().HasForeignKey(r => r.EmployeeId);
        mb.Entity<E.Staff>()
            .Navigation(s => s.RoleEntries)
            .AutoInclude();
        mb.Entity<E.StaffLocation>()
            .HasKey(l => new { l.EmployeeId, l.LocationId });
        mb.Entity<E.Staff>()
            .Navigation(s => s.LocationEntries)
            .AutoInclude();

        // Permission tables: composite PKs (no auto-discovered Id)
        mb.Entity<E.RolePermission>()
            .HasKey(r => new { r.RoleId, r.PermissionKey });
        mb.Entity<E.StaffPermissionOverride>()
            .HasKey(o => new { o.EmployeeId, o.PermissionKey });
    }
}
