using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Every money figure of a payslip is encrypted at rest, and so is a warning message (it can contain figures,
/// for example the wage in a band-gap warning). Nothing here is summed or sorted in SQL: totals are summed in memory.
/// </summary>
public class PayrollRunLineConfiguration(IFieldEncryptor encryptor) : IEntityTypeConfiguration<PayrollRunLine>
{
    public void Configure(EntityTypeBuilder<PayrollRunLine> b)
    {
        var enc = new EncryptedColumns(encryptor);
        b.ToTable("PayrollRunLines");
        // Ids are assigned by the domain, never by the database: without this, EF treats a child added to a run that is
        // already tracked as an existing row (key already set) and sends an UPDATE that matches nothing.
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.StaffId).HasMaxLength(20).IsRequired();
        b.Property(l => l.Name).HasMaxLength(120).IsRequired();
        b.Property(l => l.ExcludedReason).HasMaxLength(500);
        b.Property(l => l.PcbAmount).HasConversion(enc.NullableMoney).HasColumnType("text");
        b.Property(l => l.Gross).HasConversion(enc.Money).HasColumnType("text");
        b.Property(l => l.Net).HasConversion(enc.Money).HasColumnType("text");
        b.Property(l => l.EmployerCost).HasConversion(enc.Money).HasColumnType("text");
        b.Property(l => l.UnappliedDeductions).HasConversion(enc.Money).HasColumnType("text");
        b.Property(l => l.SignedOffAt).HasColumnType("timestamp with time zone");

        // A payroll record is kept for the retention period, so the person cannot be removed from under it.
        b.HasOne<Staff>().WithMany().HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(l => new { l.RunId, l.EmployeeId }).IsUnique();
        b.HasIndex(l => l.EmployeeId);

        b.HasMany(l => l.Items).WithOne().HasForeignKey(i => i.LineId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(l => l.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(l => l.UnpaidDays).WithOne().HasForeignKey(d => d.LineId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(l => l.UnpaidDays).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(l => l.Warnings).WithOne().HasForeignKey(w => w.LineId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(l => l.Warnings).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PayrollRunLineItemConfiguration(IFieldEncryptor encryptor) : IEntityTypeConfiguration<PayrollRunLineItem>
{
    public void Configure(EntityTypeBuilder<PayrollRunLineItem> b)
    {
        var enc = new EncryptedColumns(encryptor);
        b.ToTable("PayrollRunLineItems");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.Type).HasMaxLength(40).IsRequired();
        b.Property(i => i.Section).HasConversion<string>().HasMaxLength(30);
        b.Property(i => i.Description).HasMaxLength(300).IsRequired();
        b.Property(i => i.Amount).HasConversion(enc.Money).HasColumnType("text");
        b.HasIndex(i => i.LineId);
        // The cash advance guard asks "does any payslip line name this advance?" on every settle or delete.
        b.HasIndex(i => i.SourceId);
    }
}

public class PayrollRunUnpaidDayConfiguration(IFieldEncryptor encryptor) : IEntityTypeConfiguration<PayrollRunUnpaidDay>
{
    public void Configure(EntityTypeBuilder<PayrollRunUnpaidDay> b)
    {
        var enc = new EncryptedColumns(encryptor);
        b.ToTable("PayrollRunUnpaidDays");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.Days).HasPrecision(8, 4);
        b.Property(d => d.DeductionAmount).HasConversion(enc.Money).HasColumnType("text");
        b.HasIndex(d => d.LineId);
    }
}

public class PayrollRunWarningConfiguration(IFieldEncryptor encryptor) : IEntityTypeConfiguration<PayrollRunWarning>
{
    public void Configure(EntityTypeBuilder<PayrollRunWarning> b)
    {
        var enc = new EncryptedColumns(encryptor);
        b.ToTable("PayrollRunWarnings");
        b.HasKey(w => w.Id);
        b.Property(w => w.Id).ValueGeneratedNever();
        b.Property(w => w.Code).HasMaxLength(60).IsRequired();
        b.Property(w => w.Message).HasConversion(enc.Text).HasColumnType("text");
        b.HasIndex(w => w.LineId);
    }
}
