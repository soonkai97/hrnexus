using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.HasKey(n => n.Id);
        b.Property(n => n.Title).HasMaxLength(200).IsRequired();
        b.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        b.Property(n => n.Type).HasMaxLength(50).IsRequired();
        b.Property(n => n.RelatedId).HasMaxLength(100);
        b.HasOne(n => n.Employee).WithMany().HasForeignKey(n => n.EmployeeId);
        b.HasIndex(n => new { n.EmployeeId, n.IsRead });
        b.HasIndex(n => n.CreatedAt);
    }
}
