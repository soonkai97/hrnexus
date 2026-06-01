using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffLocationConfiguration : IEntityTypeConfiguration<StaffLocation>
{
    public void Configure(EntityTypeBuilder<StaffLocation> b)
    {
        b.ToTable("StaffLocations");
        b.HasKey(l => new { l.EmployeeId, l.LocationId });
        b.Property(l => l.LocationId).HasMaxLength(50);
    }
}
