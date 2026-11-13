using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(a => a.Id);
        b.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        b.Property(a => a.Action).HasMaxLength(100).IsRequired();
        b.Property(a => a.PerformedByName).HasMaxLength(200).IsRequired();
        b.Property(a => a.Changes);   // JSON — no length cap
        b.HasIndex(a => new { a.EntityType, a.EntityId }); // common audit query pattern
        b.HasIndex(a => a.Timestamp);
    }
}
