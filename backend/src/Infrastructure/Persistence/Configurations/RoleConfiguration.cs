using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
        // Existing rows (and any created before the app set it) default to the global cap so every job
        // starts at 30 days — the migration backfills the column with this default.
    }
}
