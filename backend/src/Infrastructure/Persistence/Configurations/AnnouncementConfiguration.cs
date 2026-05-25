using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.Title).HasMaxLength(300).IsRequired();
        b.Property(a => a.Body).HasMaxLength(4000).IsRequired();
        b.Property(a => a.TargetRoles).HasMaxLength(500).IsRequired();
        b.Property(a => a.CreatedByName).HasMaxLength(200).IsRequired();
        b.HasIndex(a => a.IsActive);
        b.HasIndex(a => a.CreatedAt);

        b.HasMany(a => a.Reads)
         .WithOne()
         .HasForeignKey(r => r.AnnouncementId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AnnouncementReadConfiguration : IEntityTypeConfiguration<AnnouncementRead>
{
    public void Configure(EntityTypeBuilder<AnnouncementRead> b)
    {
        b.ToTable("AnnouncementReads");
        b.HasKey(r => new { r.AnnouncementId, r.EmployeeId });
        b.Property(r => r.ReadAt).IsRequired();
    }
}
