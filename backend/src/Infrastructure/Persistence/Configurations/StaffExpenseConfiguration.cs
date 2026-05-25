using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffExpenseConfiguration : IEntityTypeConfiguration<StaffExpense>
{
    public void Configure(EntityTypeBuilder<StaffExpense> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Category).IsRequired().HasMaxLength(100);
        b.Property(e => e.Amount).HasColumnType("numeric(10,2)");
        b.Property(e => e.Notes).HasMaxLength(500);
        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);
        b.Property(e => e.DeletedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.DeletedBy).HasMaxLength(100);

        b.HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(e => !e.IsDeleted);

        b.HasIndex(e => new { e.StaffId, e.Year, e.Month })
         .HasDatabaseName("IX_StaffExpenses_StaffId_YearMonth");
    }
}
