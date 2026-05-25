using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class TipsAllocationConfiguration : IEntityTypeConfiguration<TipsAllocationRecord>
{
    public void Configure(EntityTypeBuilder<TipsAllocationRecord> b)
    {
        b.ToTable("TipsAllocations");
        b.HasKey(t => t.Id);
        b.Property(t => t.FullName).HasMaxLength(200).IsRequired();
        b.Property(t => t.Amount).HasPrecision(10, 2);
        b.Property(t => t.HoursWorked).HasPrecision(6, 2);
        // Composite covers single-column PoolId and EmployeeId queries
        b.HasIndex(t => new { t.PoolId, t.EmployeeId });
    }
}
