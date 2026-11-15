using HRNexus.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    public DbSet<E.Role>             Roles             => Set<E.Role>();
    public DbSet<E.SalaryProfile>    SalaryProfiles    => Set<E.SalaryProfile>();
    public DbSet<E.CompanyPolicy>    CompanyPolicies   => Set<E.CompanyPolicy>();
    public DbSet<E.LeaveTypeDefinition> LeaveTypes     => Set<E.LeaveTypeDefinition>();
    public DbSet<E.StatutoryRateTable> StatutoryRateTables => Set<E.StatutoryRateTable>();
    public DbSet<E.RolePermission>          RolePermissions          => Set<E.RolePermission>();
    public DbSet<E.StaffPermissionOverride> StaffPermissionOverrides => Set<E.StaffPermissionOverride>();
    public DbSet<E.LeaveRequest>     LeaveRequests     => Set<E.LeaveRequest>();
    public DbSet<E.LeaveEntitlement> LeaveEntitlements => Set<E.LeaveEntitlement>();
    public DbSet<E.Notification>     Notifications     => Set<E.Notification>();
    public DbSet<E.AuditLog>         AuditLogs         => Set<E.AuditLog>();
    public DbSet<E.PushSubscription> PushSubscriptions => Set<E.PushSubscription>();
    public DbSet<E.PayrollLock>          PayrollLocks           => Set<E.PayrollLock>();
    public DbSet<E.CashAdvance>          CashAdvances           => Set<E.CashAdvance>();
    public DbSet<E.PayrollRun>           PayrollRuns            => Set<E.PayrollRun>();
    public DbSet<E.PayItem>              PayItems               => Set<E.PayItem>();
    public DbSet<E.CashAdvanceRepayment> CashAdvanceRepayments  => Set<E.CashAdvanceRepayment>();

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
        // The rate table owns its bands through a backing field (no public setter), like production.
        mb.Entity<E.StatutoryRateTable>(b =>
        {
            b.HasMany(t => t.Bands).WithOne().HasForeignKey(x => x.RateTableId);
            b.Navigation(t => t.Bands).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });

        // The payroll run owns its lines and history through backing fields, like production.
        // Ids are assigned by the domain (as in production), so a child added to a tracked run is inserted, not updated.
        mb.Entity<E.PayrollRunActor>().Property(x => x.Id).ValueGeneratedNever();
        mb.Entity<E.PayrollRunLineItem>().Property(x => x.Id).ValueGeneratedNever();
        mb.Entity<E.PayrollRunUnpaidDay>().Property(x => x.Id).ValueGeneratedNever();
        mb.Entity<E.PayrollRunWarning>().Property(x => x.Id).ValueGeneratedNever();
        mb.Entity<E.PayrollRun>(b =>
        {
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Version).IsConcurrencyToken();
            b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.RunId);
            b.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
            b.HasMany(r => r.Actors).WithOne().HasForeignKey(a => a.RunId);
            b.Navigation(r => r.Actors).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });
        mb.Entity<E.PayrollRunLine>(b =>
        {
            b.Property(l => l.Id).ValueGeneratedNever();
            b.HasMany(l => l.Items).WithOne().HasForeignKey(i => i.LineId);
            b.Navigation(l => l.Items).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
            b.HasMany(l => l.UnpaidDays).WithOne().HasForeignKey(d => d.LineId);
            b.Navigation(l => l.UnpaidDays).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
            b.HasMany(l => l.Warnings).WithOne().HasForeignKey(w => w.LineId);
            b.Navigation(l => l.Warnings).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });

        // Leave types are keyed by their slug, like production.
        mb.Entity<E.LeaveTypeDefinition>().HasKey(t => t.Key);

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

        // Permission tables: composite PKs (no auto-discovered Id)
        mb.Entity<E.RolePermission>()
            .HasKey(r => new { r.RoleId, r.PermissionKey });
        mb.Entity<E.StaffPermissionOverride>()
            .HasKey(o => new { o.EmployeeId, o.PermissionKey });
    }
}
