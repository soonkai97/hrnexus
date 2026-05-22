using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class WorkLogConfiguration : IEntityTypeConfiguration<WorkLog>
{
    public void Configure(EntityTypeBuilder<WorkLog> b)
    {
        b.HasKey(w => w.Id);
        b.Property(w => w.RoleName).HasMaxLength(100).IsRequired();
        b.Property(w => w.LocationId).HasMaxLength(100).IsRequired();
        b.Property(w => w.LocationName).HasMaxLength(200).IsRequired();
        b.Property(w => w.StartTime).HasMaxLength(5).IsRequired();
        b.Property(w => w.EndTime).HasMaxLength(5);
        b.Property(w => w.HourlyRateSnapshot).HasColumnType("decimal(10,2)");
        b.Property(w => w.Notes).HasMaxLength(500);
        b.Property(w => w.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(w => w.CreatedBy).HasMaxLength(100);
        b.Property(w => w.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(w => w.ModifiedBy).HasMaxLength(100);
        b.Property(w => w.DeletedAt).HasColumnType("timestamp with time zone");
        b.Property(w => w.DeletedBy).HasMaxLength(100);
        b.HasOne(w => w.Employee).WithMany().HasForeignKey(w => w.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(w => !w.IsDeleted);
        b.HasIndex(w => w.EmployeeId);
        b.HasIndex(w => w.WorkDate);
    }
}
