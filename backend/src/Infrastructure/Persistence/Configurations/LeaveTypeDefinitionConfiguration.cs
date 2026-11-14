using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class LeaveTypeDefinitionConfiguration : IEntityTypeConfiguration<LeaveTypeDefinition>
{
    public void Configure(EntityTypeBuilder<LeaveTypeDefinition> b)
    {
        // The key is the value already stored on every leave request (LeaveRequest.LeaveType, 20 characters).
        b.HasKey(t => t.Key);
        b.Property(t => t.Key).HasMaxLength(20).ValueGeneratedNever();
        b.Property(t => t.Label).HasMaxLength(40).IsRequired();
        b.Property(t => t.Colour).HasMaxLength(10).IsRequired();
        b.Property(t => t.Pool).HasMaxLength(10).IsRequired();
        b.Property(t => t.AllowanceDays).HasPrecision(5, 1);
        b.Property(t => t.MaxDaysPerRequest).HasPrecision(5, 1);
        b.Property(t => t.UpdatedAt).HasColumnType("timestamp with time zone");
        b.Property(t => t.UpdatedBy).HasMaxLength(100);
        b.HasIndex(t => t.SortOrder);
    }
}
