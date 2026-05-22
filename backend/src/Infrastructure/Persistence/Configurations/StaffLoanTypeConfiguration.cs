using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class StaffLoanTypeConfiguration : IEntityTypeConfiguration<StaffLoanType>
{
    public void Configure(EntityTypeBuilder<StaffLoanType> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Key).IsRequired().HasMaxLength(50);
        b.Property(e => e.Label).IsRequired().HasMaxLength(100);
        b.Property(e => e.IsAdvance).IsRequired();
        b.Property(e => e.DisplayOrder).IsRequired();
        b.Property(e => e.IsActive).IsRequired();
        b.Property(e => e.IsSystem).IsRequired();
        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("IX_StaffLoanTypes_Key");
    }
}
