using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class LeaveEntitlementConfiguration : IEntityTypeConfiguration<LeaveEntitlement>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlement> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.AnnualHours).HasPrecision(8, 2);
        b.Property(e => e.SickHours).HasPrecision(8, 2);
        b.Property(e => e.CarryOverHours).HasPrecision(8, 2);
        b.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        b.HasIndex(e => new { e.EmployeeId, e.Year }).IsUnique();
    }
}
