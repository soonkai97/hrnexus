using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRNexus.Application.Common.Interfaces;

public interface IAppDbContext
{
    /// <summary>
    /// Begins a RepeatableRead transaction for balance-check TOCTOU protection.
    /// Returns null when the underlying provider does not support transactions (e.g. InMemory in tests).
    /// </summary>
    Task<IDbContextTransaction?> BeginRepeatableReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Begins a Serializable transaction. Used for the leave balance check where two concurrent
    /// approvals inserting different rows must not both pass a check-then-insert (RepeatableRead
    /// snapshot can't catch that; Serializable SSI aborts one, which the execution strategy retries).
    /// Returns null on non-relational providers (InMemory tests).
    /// </summary>
    Task<IDbContextTransaction?> BeginSerializableAsync(CancellationToken ct = default);

    /// <summary>
    /// Wraps an operation in the configured execution strategy (required when using
    /// user-initiated transactions with NpgsqlRetryingExecutionStrategy).
    /// On InMemory (tests) the non-retrying strategy executes the operation directly.
    /// </summary>
    Task<T> ExecuteInStrategyAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    DbSet<HRNexus.Domain.Entities.Staff>    Staff      { get; }
    DbSet<HRNexus.Domain.Entities.StaffRole> StaffRoles { get; }
    DbSet<Role>             Roles             { get; }
    DbSet<SalaryProfile>    SalaryProfiles    { get; }
    DbSet<CompanyPolicy>    CompanyPolicies   { get; }
    DbSet<StatutoryRateTable> StatutoryRateTables { get; }
    DbSet<RolePermission>          RolePermissions          { get; }
    DbSet<StaffPermissionOverride> StaffPermissionOverrides { get; }
    DbSet<LeaveRequest>     LeaveRequests     { get; }
    DbSet<LeaveEntitlement> LeaveEntitlements { get; }
    DbSet<Notification>       Notifications       { get; }
    DbSet<AuditLog>           AuditLogs           { get; }
    DbSet<PushSubscription>   PushSubscriptions   { get; }
    DbSet<PayrollLock>           PayrollLocks            { get; }
    DbSet<HRNexus.Domain.Entities.CashAdvance> CashAdvances { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Detaches all tracked entities. Call at the start of each attempt inside
    /// <see cref="ExecuteInStrategyAsync"/> so a retried delegate doesn't collide with entities the
    /// previous (rolled-back) attempt left in the change tracker.
    /// </summary>
    void ClearChangeTracker();
}
