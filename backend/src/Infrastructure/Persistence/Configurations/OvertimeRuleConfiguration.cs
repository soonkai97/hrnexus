using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class OvertimeRuleConfiguration : IEntityTypeConfiguration<OvertimeRule>
{
    // Stable seed ID so migrations are deterministic
    private static readonly Guid DefaultRuleId =
        new("11111111-0000-0000-0000-000000000001");

    public void Configure(EntityTypeBuilder<OvertimeRule> b)
    {
        b.HasKey(r => r.Id);
        b.Property(r => r.Role).HasMaxLength(100).IsRequired();
        b.Property(r => r.ThresholdHours).HasPrecision(10, 2).IsRequired();
        b.Property(r => r.Multiplier).HasPrecision(10, 4).IsRequired();
        b.Property(r => r.CreatedAt).IsRequired();

        // Seed a catch-all default: 1.5× after 8 hours, applies to every role
        b.HasData(new
        {
            Id             = DefaultRuleId,
            Role           = "*",
            ThresholdHours = 8m,
            Multiplier     = 1.5m,
            IsActive       = true,
            CreatedAt      = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
