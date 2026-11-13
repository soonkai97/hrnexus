using System.Globalization;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRNexus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Salary, account number and holder are encrypted at rest (AES-GCM through the same field encryptor as
/// the Staff PII). None of them is filtered or sorted on, so randomised encryption is safe; the rows are
/// found by employee and effective date, which stay in plain columns.
/// </summary>
public class SalaryProfileConfiguration(IFieldEncryptor encryptor) : IEntityTypeConfiguration<SalaryProfile>
{
    public void Configure(EntityTypeBuilder<SalaryProfile> b)
    {
        var stringCipher = new ValueConverter<string?, string?>(
            v => encryptor.Encrypt(v),
            v => encryptor.Decrypt(v));
        var moneyCipher = new ValueConverter<decimal, string>(
            v => encryptor.Encrypt(v.ToString(CultureInfo.InvariantCulture))!,
            v => decimal.Parse(encryptor.Decrypt(v)!, CultureInfo.InvariantCulture));

        b.HasKey(p => p.Id);
        b.Property(p => p.BasicSalary).HasConversion(moneyCipher).HasColumnType("text");
        b.Property(p => p.PayFrequency).HasMaxLength(10).IsRequired();
        b.Property(p => p.BankName).HasMaxLength(100);
        b.Property(p => p.BankAccount).HasConversion(stringCipher).HasColumnType("text");
        b.Property(p => p.BankHolder).HasConversion(stringCipher).HasColumnType("text");
        b.Property(p => p.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(p => p.CreatedBy).HasMaxLength(100);

        // Pay history is a payroll record (kept for the retention period), so it is not removed with the person.
        b.HasOne<Staff>().WithMany().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.EmployeeId, p.EffectiveFrom }).IsUnique();
    }
}
