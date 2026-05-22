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
    DbSet<StaffEntry>       StaffEntries       { get; }
    DbSet<StaffExpense>     StaffExpenses      { get; }
    DbSet<StaffLoan>        StaffLoans         { get; }
    DbSet<StaffWorkType>         StaffWorkTypes          { get; }
    DbSet<StaffSettlement>       StaffSettlements        { get; }
    DbSet<StaffExpenseCategory>  StaffExpenseCategories  { get; }
    DbSet<StaffLoanType>         StaffLoanTypes          { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
