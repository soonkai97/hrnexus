using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffEntryConfiguration : IEntityTypeConfiguration<StaffEntry>
{
    public void Configure(EntityTypeBuilder<StaffEntry> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.WorkType).IsRequired().HasMaxLength(50);
        b.Property(e => e.VenueUnits).HasColumnType("numeric(10,2)");
        b.Property(e => e.VenueRate).HasColumnType("numeric(10,2)");
        b.Property(e => e.VenueEarnings).HasColumnType("numeric(10,2)");
        b.Property(e => e.PrivateGross).HasColumnType("numeric(10,2)");
        b.Property(e => e.PrivateNet).HasColumnType("numeric(10,2)");
        b.Property(e => e.Settlement).IsRequired().HasMaxLength(50);
        b.Property(e => e.Notes).HasMaxLength(500);
        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);
        b.Property(e => e.DeletedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.DeletedBy).HasMaxLength(100);

        b.HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(e => !e.IsDeleted);

        b.HasIndex(e => new { e.StaffId, e.EntryDate })
         .HasDatabaseName("IX_StaffEntries_StaffId_EntryDate");
    }
}
