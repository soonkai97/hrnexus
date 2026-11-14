using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Statutory rate rows and their bands. Rates, rounding, ceilings and bands are immutable after insert (the entity
/// has no setters for them); a correction is a new row with a later effective date. Rates are not secret, so they
/// are stored in plain columns.
/// </summary>
public class StatutoryRateTableConfiguration : IEntityTypeConfiguration<StatutoryRateTable>
{
    public void Configure(EntityTypeBuilder<StatutoryRateTable> b)
    {
        b.ToTable("StatutoryRateTables");
        b.HasKey(t => t.Id);
        b.Property(t => t.Scheme).HasMaxLength(30).IsRequired();
        b.Property(t => t.CitizenshipClass).HasMaxLength(20).IsRequired();
        b.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(t => t.EmployerRateMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(t => t.Rounding).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(t => t.WageCeiling).HasPrecision(12, 2);
        b.Property(t => t.EmployeeRate).HasPrecision(18, 4);
        b.Property(t => t.EmployerRate).HasPrecision(18, 4);
        b.Property(t => t.EmployerRateThreshold).HasPrecision(12, 2);
        b.Property(t => t.EmployerRateAbove).HasPrecision(18, 4);
        b.Property(t => t.Amount).HasPrecision(12, 2);
        b.Property(t => t.VerifiedBy).HasMaxLength(100);
        b.Property(t => t.VerificationNote).HasMaxLength(500);
        b.Property(t => t.Source).HasMaxLength(200).IsRequired();
        b.Property(t => t.Note).HasMaxLength(500);
        b.Property(t => t.VerifiedAt).HasColumnType("timestamp with time zone");
        b.Property(t => t.CreatedAt).HasColumnType("timestamp with time zone");

        b.HasMany(t => t.Bands).WithOne().HasForeignKey(x => x.RateTableId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(t => t.Bands).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        // One row per scheme, class, starting age and effective date: a new rate is a new effective date.
        b.HasIndex(t => new { t.Scheme, t.CitizenshipClass, t.AgeFrom, t.EffectiveFrom }).IsUnique();
    }
}

public class StatutoryRateBandConfiguration : IEntityTypeConfiguration<StatutoryRateBand>
{
    public void Configure(EntityTypeBuilder<StatutoryRateBand> b)
    {
        b.ToTable("StatutoryRateBands");
        b.HasKey(x => x.Id);
        b.Property(x => x.WageFrom).HasPrecision(12, 2);
        b.Property(x => x.WageTo).HasPrecision(12, 2);
        b.Property(x => x.EmployeeAmount).HasPrecision(12, 2);
        b.Property(x => x.EmployerAmount).HasPrecision(12, 2);
        b.HasIndex(x => new { x.RateTableId, x.WageFrom }).IsUnique();
    }
}
