using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class RoleRateConfiguration : IEntityTypeConfiguration<RoleRate>
{
    public void Configure(EntityTypeBuilder<RoleRate> b)
    {
        b.HasKey(r => r.Id);
        b.Property(r => r.RoleName).HasMaxLength(50).IsRequired();
        b.Property(r => r.Rate).HasColumnType("decimal(10,2)");
        b.Property(r => r.EffectiveOn).IsRequired();
        // "Rate as of date" lookup: greatest EffectiveOn <= D for a role.
        b.HasIndex(r => new { r.RoleName, r.EffectiveOn });
    }
}
