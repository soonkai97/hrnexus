using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class CashAdvanceConfiguration : IEntityTypeConfiguration<CashAdvance>
{
    public void Configure(EntityTypeBuilder<CashAdvance> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.Amount).HasPrecision(10, 2);
        b.Property(c => c.Status).HasMaxLength(20).IsRequired();
        b.Property(c => c.Reason).HasMaxLength(500);
        b.Property(c => c.CreatedBy).HasMaxLength(100);
        b.HasOne(c => c.Employee).WithMany().HasForeignKey(c => c.EmployeeId);
        b.HasIndex(c => c.EmployeeId);
        b.HasIndex(c => c.Status);
    }
}
