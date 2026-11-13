using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class PayrollLockConfiguration : IEntityTypeConfiguration<PayrollLock>
{
    public void Configure(EntityTypeBuilder<PayrollLock> b)
    {
        b.ToTable("PayrollLocks");
        b.HasKey(p => p.Id);
        b.Property(p => p.LockedBy).IsRequired().HasMaxLength(100);
        b.Property(p => p.Note).HasMaxLength(500);
        b.Property(p => p.LockedAt).HasColumnType("timestamp with time zone");

        // One lock per calendar month; the unique index also makes a concurrent double-lock fail fast.
        b.HasIndex(p => new { p.Year, p.Month }).IsUnique().HasDatabaseName("IX_PayrollLocks_Year_Month");
    }
}
