using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<NightclubWorkforce.Domain.Entities.Staff>    Staff      { get; }
    DbSet<NightclubWorkforce.Domain.Entities.StaffRole> StaffRoles { get; }
    DbSet<Location>         Locations         { get; }
    DbSet<Role>             Roles             { get; }
    DbSet<NightclubWorkforce.Domain.Entities.WorkLog> WorkLogs { get; }
    DbSet<LeaveRequest>     LeaveRequests     { get; }
    DbSet<LeaveEntitlement> LeaveEntitlements { get; }
    DbSet<Notification>       Notifications       { get; }
    DbSet<OvertimeRule>       OvertimeRules       { get; }
    DbSet<Announcement>       Announcements       { get; }
    DbSet<AnnouncementRead>   AnnouncementReads   { get; }
    DbSet<AuditLog>           AuditLogs           { get; }
    DbSet<TipsPool>             TipsPools           { get; }
    DbSet<TipsAllocationRecord> TipsAllocations     { get; }
    DbSet<PushSubscription>   PushSubscriptions   { get; }
    DbSet<StaffMessage>       StaffMessages        { get; }
    DbSet<FeatureFlag>        FeatureFlags         { get; }
    DbSet<HostessEntry>       HostessEntries       { get; }
    DbSet<HostessExpense>     HostessExpenses      { get; }
    DbSet<HostessLoan>        HostessLoans         { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
