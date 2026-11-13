using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Common;
using HRNexus.Domain.Entities;
using HRNexus.Infrastructure.Persistence.Configurations;

namespace HRNexus.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserService currentUser, IFieldEncryptor encryptor)
    : DbContext(options), IAppDbContext
{
    public DbSet<Staff>            Staff             => Set<Staff>();
    public DbSet<StaffRole>        StaffRoles        => Set<StaffRole>();
    public DbSet<StaffLocation>    StaffLocations    => Set<StaffLocation>();
    public DbSet<Location>         Locations         => Set<Location>();
    public DbSet<Role>             Roles             => Set<Role>();
    public DbSet<RoleRate>         RoleRates         => Set<RoleRate>();
    public DbSet<RolePermission>          RolePermissions          => Set<RolePermission>();
    public DbSet<StaffPermissionOverride> StaffPermissionOverrides => Set<StaffPermissionOverride>();
    public DbSet<LeaveRequest>     LeaveRequests     => Set<LeaveRequest>();
    public DbSet<LeaveEntitlement> LeaveEntitlements => Set<LeaveEntitlement>();
    public DbSet<Notification>     Notifications     => Set<Notification>();
    public DbSet<AuditLog>          AuditLogs          => Set<AuditLog>();
    public DbSet<PushSubscription>     PushSubscriptions   => Set<PushSubscription>();
    public DbSet<PayrollLock>          PayrollLocks           => Set<PayrollLock>();
    public DbSet<CashAdvance>          CashAdvances           => Set<CashAdvance>();

    /// <summary>
    /// Begins a RepeatableRead transaction for leave-balance TOCTOU protection.
    /// Returns null when the underlying provider does not support transactions (never happens
    /// in production — guard exists so InMemory-backed tests can call this safely).
    /// </summary>
    public async Task<IDbContextTransaction?> BeginRepeatableReadAsync(CancellationToken ct = default)
    {
        if (!Database.IsRelational()) return null;
        return await Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    }

    public async Task<IDbContextTransaction?> BeginSerializableAsync(CancellationToken ct = default)
    {
        if (!Database.IsRelational()) return null;
        return await Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }

    public Task<T> ExecuteInStrategyAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        => Database.CreateExecutionStrategy().ExecuteAsync(operation, ct);

    public void ClearChangeTracker() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfiguration(new StaffConfiguration(encryptor));
        mb.ApplyConfiguration(new StaffRoleConfiguration());
        mb.ApplyConfiguration(new StaffLocationConfiguration());
        mb.ApplyConfiguration(new LocationConfiguration());
        mb.ApplyConfiguration(new RoleConfiguration());
        mb.ApplyConfiguration(new RoleRateConfiguration());
        mb.ApplyConfiguration(new RolePermissionConfiguration());
        mb.ApplyConfiguration(new StaffPermissionOverrideConfiguration());
        mb.ApplyConfiguration(new LeaveRequestConfiguration());
        mb.ApplyConfiguration(new LeaveEntitlementConfiguration());
        mb.ApplyConfiguration(new NotificationConfiguration());
        mb.ApplyConfiguration(new AuditLogConfiguration());
        mb.ApplyConfiguration(new PushSubscriptionConfiguration());
        mb.ApplyConfiguration(new PayrollLockConfiguration());
        mb.ApplyConfiguration(new CashAdvanceConfiguration());
        SeedData.Seed(mb);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now  = DateTime.UtcNow;
        var user = currentUser.UserName ?? "System";

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IHasAudit)
            {
                if (entry.State == EntityState.Added)
                {
                    // Only stamp if not already set by the domain factory method
                    TrySet(entry, "CreatedAt",  now,  overwrite: false);
                    TrySet(entry, "CreatedBy",  user, overwrite: false);
                }
                if (entry.State is EntityState.Modified or EntityState.Added)
                {
                    TrySet(entry, "ModifiedAt", now);
                    TrySet(entry, "ModifiedBy", user);
                }
            }

            // Soft delete: intercept EF Remove() and convert to IsDeleted flag
            if (entry.Entity is ISoftDelete && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                TrySet(entry, "IsDeleted", true);
                TrySet(entry, "DeletedAt", now);
                TrySet(entry, "DeletedBy", user);
            }
        }

        return await base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sets a mapped EF Core property by name without requiring a concrete C# property accessor.
    /// When <paramref name="overwrite"/> is false, skips if the property already has a non-default value.
    /// </summary>
    private static void TrySet(EntityEntry entry, string name, object value, bool overwrite = true)
    {
        var prop = entry.Properties.FirstOrDefault(p => p.Metadata.Name == name);
        if (prop == null) return;
        if (!overwrite && prop.CurrentValue is not (null or "" or (DateTime { Ticks: 0 }))) return;
        prop.CurrentValue = value;
    }
}
