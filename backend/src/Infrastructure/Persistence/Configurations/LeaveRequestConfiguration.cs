using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.HasKey(l => l.Id);
        b.Property(l => l.LeaveType).HasMaxLength(20).IsRequired();
        b.Property(l => l.Status).HasMaxLength(20).IsRequired();
        b.Property(l => l.Notes).HasMaxLength(500);
        b.Property(l => l.RejectionReason).HasMaxLength(500);
        b.Property(l => l.StartTime).HasMaxLength(5);
        b.Property(l => l.EndTime).HasMaxLength(5);
        b.Property(l => l.Hours).HasPrecision(8, 2);
        b.HasOne(l => l.Employee).WithMany().HasForeignKey(l => l.EmployeeId);
        b.HasIndex(l => l.EmployeeId);
        b.HasIndex(l => l.StartDate);
        // xmin is a PostgreSQL system column (uint / xid type) that increments on every UPDATE.
        // Declaring it as a shadow property with IsConcurrencyToken() makes EF add
        // "WHERE xmin = @p" to every UPDATE so concurrent admin approval + auto-reject
        // can't silently overwrite each other.  No migration is needed — xmin already
        // exists on every PostgreSQL table row; this just teaches EF to SELECT and check it.
        b.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRequired()
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
