using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class StaffRoleConfiguration : IEntityTypeConfiguration<StaffRole>
{
    public void Configure(EntityTypeBuilder<StaffRole> b)
    {
        b.ToTable("StaffRoles");
        b.HasKey(r => new { r.EmployeeId, r.Role });
        b.Property(r => r.Role).HasMaxLength(50).IsRequired();
    }
}
