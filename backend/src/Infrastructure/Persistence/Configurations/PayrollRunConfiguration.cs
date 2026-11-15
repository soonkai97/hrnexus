using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.ToTable("PayrollRuns");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.State).HasMaxLength(20).IsRequired();
        // The aggregate bumps Version on every change to the run or its lines; as a concurrency token a stale
        // save (two people editing, or approving over an edit) updates zero rows and is refused.
        b.Property(r => r.Version).IsConcurrencyToken();
        b.Property(r => r.CalculatorVersion).HasMaxLength(20).IsRequired();
        b.Property(r => r.Divisor).HasPrecision(10, 4);
        b.Property(r => r.HoursPerWorkingDay).HasPrecision(10, 4);
        b.Property(r => r.DeductionCapRate).HasPrecision(10, 4);
        b.Property(r => r.ExportedAt).HasColumnType("timestamp with time zone");
        b.Property(r => r.CreatedAt).HasColumnType("timestamp with time zone");

        // One run per calendar month; the unique index also makes a concurrent double start fail fast.
        b.HasIndex(r => new { r.Year, r.Month }).IsUnique().HasDatabaseName("IX_PayrollRuns_Year_Month");

        b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.RunId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(r => r.Actors).WithOne().HasForeignKey(a => a.RunId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Actors).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PayrollRunActorConfiguration : IEntityTypeConfiguration<PayrollRunActor>
{
    public void Configure(EntityTypeBuilder<PayrollRunActor> b)
    {
        b.ToTable("PayrollRunActors");
        // Ids are assigned by the domain, never by the database: without this, EF treats a child added to a run that is
        // already tracked as an existing row (key already set) and sends an UPDATE that matches nothing.
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Action).HasMaxLength(20).IsRequired();
        b.Property(a => a.Reason).HasMaxLength(500);
        b.Property(a => a.At).HasColumnType("timestamp with time zone");
        b.HasIndex(a => new { a.RunId, a.StaffId });
    }
}
