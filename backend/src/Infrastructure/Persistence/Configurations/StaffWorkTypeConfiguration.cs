using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class StaffWorkTypeConfiguration : IEntityTypeConfiguration<StaffWorkType>
{
    public void Configure(EntityTypeBuilder<StaffWorkType> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Key).IsRequired().HasMaxLength(50);
        b.Property(e => e.Label).IsRequired().HasMaxLength(100);
        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);

        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("IX_StaffWorkTypes_Key");
    }
}
