using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> b)
    {
        b.HasKey(p => p.Id);
        b.Property(p => p.Endpoint).HasMaxLength(1000).IsRequired();
        b.Property(p => p.P256dh).HasMaxLength(200).IsRequired();
        b.Property(p => p.Auth).HasMaxLength(100).IsRequired();
        // Composite unique index covers single-column EmployeeId queries
        b.HasIndex(p => new { p.EmployeeId, p.Endpoint }).IsUnique();
    }
}
