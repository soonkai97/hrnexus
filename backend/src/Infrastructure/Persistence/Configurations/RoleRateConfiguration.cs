using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class RoleRateConfiguration : IEntityTypeConfiguration<RoleRate>
{
    public void Configure(EntityTypeBuilder<RoleRate> b)
    {
        b.HasKey(r => r.Id);
        b.Property(r => r.Rate).HasColumnType("decimal(10,2)");
        b.Property(r => r.EffectiveOn).IsRequired();
        // "Rate as of date" lookup: greatest EffectiveOn <= D for a role.
        b.HasIndex(r => new { r.RoleId, r.EffectiveOn });
        // FK to Role — deleting a role cascades away its rate-history rows.
        b.HasOne<Role>()
            .WithMany()
            .HasForeignKey(r => r.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
