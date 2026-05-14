using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasKey(r => r.Name);
        b.Property(r => r.Name).HasMaxLength(50).IsRequired();
        b.Property(r => r.HourlyRate).HasColumnType("decimal(10,2)");
    }
}
