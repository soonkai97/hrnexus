using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasKey(r => r.Id);
        b.Property(r => r.Name).HasMaxLength(50).IsRequired();
        // Name is now a normal display column but must stay unique — role names are still the
        // human identity (auth matches on them, the create-role dup-check keys on them).
        b.HasIndex(r => r.Name).IsUnique();
        b.Property(r => r.NameZh).HasMaxLength(100);
        b.Property(r => r.NameVi).HasMaxLength(100);
        b.Property(r => r.HourlyRate).HasColumnType("decimal(10,2)");
        b.Property(r => r.PayType).HasMaxLength(10).IsRequired();
        // Existing rows (and any created before the app set it) default to the global cap so every job
        // starts at 30 days — the migration backfills the column with this default.
        b.Property(r => r.MaxShiftDays).HasDefaultValue(HRNexus.Domain.Constants.AppConstants.WorkLog.MaxShiftDays);
    }
}
