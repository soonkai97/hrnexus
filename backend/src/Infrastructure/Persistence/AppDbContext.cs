using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;
using NightclubWorkforce.Infrastructure.Persistence.Configurations;

namespace NightclubWorkforce.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<Staff>            Staff             => Set<Staff>();
    public DbSet<StaffRole>        StaffRoles        => Set<StaffRole>();
    public DbSet<Location>         Locations         => Set<Location>();
    public DbSet<Role>             Roles             => Set<Role>();
    public DbSet<WorkLog>          WorkLogs          => Set<WorkLog>();
    public DbSet<LeaveRequest>     LeaveRequests     => Set<LeaveRequest>();
    public DbSet<LeaveEntitlement> LeaveEntitlements => Set<LeaveEntitlement>();
    public DbSet<Notification>     Notifications     => Set<Notification>();
    public DbSet<OvertimeRule>     OvertimeRules     => Set<OvertimeRule>();
    public DbSet<Announcement>      Announcements      => Set<Announcement>();
    public DbSet<AnnouncementRead>  AnnouncementReads  => Set<AnnouncementRead>();
    public DbSet<AuditLog>          AuditLogs          => Set<AuditLog>();
    public DbSet<TipsPool>             TipsPools           => Set<TipsPool>();
    public DbSet<TipsAllocationRecord> TipsAllocations     => Set<TipsAllocationRecord>();
    public DbSet<PushSubscription>     PushSubscriptions   => Set<PushSubscription>();
    public DbSet<StaffMessage>       StaffMessages        => Set<StaffMessage>();
    public DbSet<FeatureFlag>        FeatureFlags         => Set<FeatureFlag>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfiguration(new StaffConfiguration());
        mb.ApplyConfiguration(new StaffRoleConfiguration());
        mb.ApplyConfiguration(new LocationConfiguration());
        mb.ApplyConfiguration(new RoleConfiguration());
        mb.ApplyConfiguration(new WorkLogConfiguration());
        mb.ApplyConfiguration(new LeaveRequestConfiguration());
        mb.ApplyConfiguration(new LeaveEntitlementConfiguration());
        mb.ApplyConfiguration(new NotificationConfiguration());
        mb.ApplyConfiguration(new OvertimeRuleConfiguration());
        mb.ApplyConfiguration(new AnnouncementConfiguration());
        mb.ApplyConfiguration(new AnnouncementReadConfiguration());
        mb.ApplyConfiguration(new AuditLogConfiguration());
        mb.ApplyConfiguration(new PushSubscriptionConfiguration());
        mb.ApplyConfiguration(new TipsPoolConfiguration());
        mb.ApplyConfiguration(new TipsAllocationConfiguration());
        mb.ApplyConfiguration(new StaffMessageConfiguration());
        mb.ApplyConfiguration(new FeatureFlagConfiguration());
        SeedData.Seed(mb);
    }
}
