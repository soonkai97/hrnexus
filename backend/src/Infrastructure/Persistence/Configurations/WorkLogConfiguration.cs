using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

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
        // EndDate is the actual date the shift ended (null while active). It may differ from WorkDate
        // for overnight (WorkDate+1) or, after an admin multi-day correction, several days later.
        b.Property(w => w.EndDate);
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
        // Partial unique index: prevents duplicate active shifts per employee at the DB level.
        // Filter includes IsDeleted = false so soft-deleted active shifts don't block new starts.
        b.HasIndex(w => w.EmployeeId)
            .HasFilter("\"EndTime\" IS NULL AND \"IsDeleted\" = false")
            .IsUnique()
            .HasDatabaseName("IX_WorkLogs_EmployeeId_Active");
        // Composite index for per-employee date-range queries used by the overlap check
        // in CreateManualWorkLogCommand / UpdateWorkLogCommand. Not unique — multiple shifts
        // per day are allowed as long as they don't overlap in time.
        b.HasIndex(w => new { w.EmployeeId, w.WorkDate })
            .HasDatabaseName("IX_WorkLogs_EmployeeId_WorkDate");
        // Optimistic concurrency: xmin is a PostgreSQL system column that bumps on every UPDATE.
        // EF adds "WHERE xmin = @p" to UPDATEs so two concurrent end/edit operations on the same
        // shift can't silently last-write-win — the loser gets DbUpdateConcurrencyException.
        // No migration needed; xmin already exists on every row.
        b.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRequired()
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
