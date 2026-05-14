using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class LeaveEntitlementConfiguration : IEntityTypeConfiguration<LeaveEntitlement>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlement> b)
    {
        b.HasKey(e => e.Id);
        b.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        b.HasIndex(e => new { e.EmployeeId, e.Year }).IsUnique();
    }
}
