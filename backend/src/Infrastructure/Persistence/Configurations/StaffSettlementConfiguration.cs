using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class StaffSettlementConfiguration : IEntityTypeConfiguration<StaffSettlement>
{
    public void Configure(EntityTypeBuilder<StaffSettlement> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Key).IsRequired().HasMaxLength(50);
        b.Property(e => e.Label).IsRequired().HasMaxLength(100);
        b.Property(e => e.Severity).IsRequired().HasMaxLength(20);

        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("IX_StaffSettlements_Key");
    }
}
