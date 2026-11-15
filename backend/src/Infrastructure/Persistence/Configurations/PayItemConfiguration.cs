using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class PayItemConfiguration(IFieldEncryptor encryptor) : IEntityTypeConfiguration<PayItem>
{
    public void Configure(EntityTypeBuilder<PayItem> b)
    {
        var enc = new EncryptedColumns(encryptor);
        b.ToTable("PayItems");
        b.HasKey(p => p.Id);
        b.Property(p => p.Kind).HasMaxLength(20).IsRequired();
        b.Property(p => p.Reason).HasMaxLength(200).IsRequired();
        b.Property(p => p.Amount).HasConversion(enc.Money).HasColumnType("text");
        b.Property(p => p.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(p => p.CreatedBy).HasMaxLength(100);
        b.HasOne<Staff>().WithMany().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.EmployeeId);
    }
}

public class CashAdvanceRepaymentConfiguration : IEntityTypeConfiguration<CashAdvanceRepayment>
{
    public void Configure(EntityTypeBuilder<CashAdvanceRepayment> b)
    {
        b.ToTable("CashAdvanceRepayments");
        b.HasKey(r => r.Id);
        b.Property(r => r.Amount).HasPrecision(10, 2);
        b.Property(r => r.CreatedAt).HasColumnType("timestamp with time zone");
        // Repayments are history: the advance and the run cannot be removed from under them.
        b.HasOne<CashAdvance>().WithMany().HasForeignKey(r => r.CashAdvanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(r => r.RunId).OnDelete(DeleteBehavior.Restrict);
        // One repayment per advance per run.
        b.HasIndex(r => new { r.CashAdvanceId, r.RunId }).IsUnique();
        b.HasIndex(r => r.RunId);
    }
}
