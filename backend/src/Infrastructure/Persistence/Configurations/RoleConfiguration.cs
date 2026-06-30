using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasKey(r => r.Name);
        b.Property(r => r.Name).HasMaxLength(50).IsRequired();
        b.Property(r => r.NameZh).HasMaxLength(100);
        b.Property(r => r.NameVi).HasMaxLength(100);
        b.Property(r => r.HourlyRate).HasColumnType("decimal(10,2)");
        b.Property(r => r.PayType).HasMaxLength(10).IsRequired();
    }
}
