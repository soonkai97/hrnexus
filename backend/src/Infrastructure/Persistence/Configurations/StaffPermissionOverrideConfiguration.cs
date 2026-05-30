using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffPermissionOverrideConfiguration : IEntityTypeConfiguration<StaffPermissionOverride>
{
    public void Configure(EntityTypeBuilder<StaffPermissionOverride> b)
    {
        b.ToTable("StaffPermissionOverrides");
        b.HasKey(o => new { o.EmployeeId, o.PermissionKey });
        b.Property(o => o.PermissionKey).HasMaxLength(100).IsRequired();
        b.Property(o => o.Granted).IsRequired();
    }
}
