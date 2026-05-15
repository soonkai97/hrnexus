using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class TipsPoolConfiguration : IEntityTypeConfiguration<TipsPool>
{
    public void Configure(EntityTypeBuilder<TipsPool> b)
    {
        b.ToTable("TipsPools");
        b.HasKey(t => t.Id);
        b.Property(t => t.LocationName).HasMaxLength(100).IsRequired();
        b.Property(t => t.SplitMethod).HasMaxLength(20).IsRequired();
        b.Property(t => t.TotalAmount).HasPrecision(10, 2);
        b.Property(t => t.IsDistributed).HasDefaultValue(false);
        b.HasIndex(t => t.ShiftDate);
        b.HasIndex(t => t.LocationId);
        // Prevent double-distribution for the same location on the same date
        b.HasIndex(t => new { t.LocationId, t.ShiftDate }).IsUnique();
    }
}
