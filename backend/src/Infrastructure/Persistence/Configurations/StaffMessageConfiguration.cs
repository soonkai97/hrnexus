using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class StaffMessageConfiguration : IEntityTypeConfiguration<StaffMessage>
{
    public void Configure(EntityTypeBuilder<StaffMessage> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.SenderName).IsRequired().HasMaxLength(200);
        b.Property(x => x.RecipientName).HasMaxLength(200);
        b.Property(x => x.Content).IsRequired().HasMaxLength(2000);
        b.HasIndex(x => x.SenderId);
        b.HasIndex(x => x.RecipientId);
        b.HasIndex(x => x.SentAt);
    }
}
