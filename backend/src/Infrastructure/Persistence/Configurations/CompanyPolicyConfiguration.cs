using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class CompanyPolicyConfiguration : IEntityTypeConfiguration<CompanyPolicy>
{
    public void Configure(EntityTypeBuilder<CompanyPolicy> b)
    {
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();   // always the single row, Id = 1
        b.Property(p => p.WorkdayHours).HasPrecision(4, 2);
        b.Property(p => p.CarryOverMode).HasMaxLength(10).IsRequired();
        b.Property(p => p.CarryOverMaxHours).HasPrecision(6, 2);
        b.Property(p => p.UnpaidLeaveDivisor).HasPrecision(4, 2);
        b.Property(p => p.UpdatedAt).HasColumnType("timestamp with time zone");
        b.Property(p => p.UpdatedBy).HasMaxLength(100);
        // One row per installation: the key can only ever be 1.
        b.ToTable(t => t.HasCheckConstraint("CK_CompanyPolicy_SingleRow", "\"Id\" = 1"));
    }
}
