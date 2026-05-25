using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

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
    }
}
