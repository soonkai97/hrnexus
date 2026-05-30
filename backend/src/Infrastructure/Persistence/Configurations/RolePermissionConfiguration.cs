using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(r => new { r.RoleName, r.PermissionKey });
        b.Property(r => r.RoleName).HasMaxLength(50).IsRequired();
        b.Property(r => r.PermissionKey).HasMaxLength(100).IsRequired();
    }
}
