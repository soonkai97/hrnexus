using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).HasMaxLength(50);
        b.Property(l => l.Name).HasMaxLength(100).IsRequired();
        b.Property(l => l.ImagePath).HasMaxLength(200);
        b.HasQueryFilter(l => !l.IsDeleted);
        // Partial unique index — only enforce uniqueness on active (non-deleted) locations
        // so a deleted name can be reused without FK-style conflicts.
        b.HasIndex(l => l.Name)
         .IsUnique()
         .HasFilter("\"IsDeleted\" = false")
         .HasDatabaseName("IX_Locations_Name_Active");
    }
}
