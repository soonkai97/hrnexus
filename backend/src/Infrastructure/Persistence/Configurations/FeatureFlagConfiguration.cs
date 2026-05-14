using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> b)
    {
        b.ToTable("FeatureFlags");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
    }
}
