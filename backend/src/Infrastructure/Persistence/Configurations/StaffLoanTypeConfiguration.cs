using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

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
        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);

        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("IX_StaffLoanTypes_Key");
    }
}
