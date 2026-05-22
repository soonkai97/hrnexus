using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class HostessExpenseConfiguration : IEntityTypeConfiguration<HostessExpense>
{
    public void Configure(EntityTypeBuilder<HostessExpense> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Category).IsRequired().HasMaxLength(100);
        b.Property(e => e.Amount).HasColumnType("numeric(10,2)");
        b.Property(e => e.Notes).HasMaxLength(500);
        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone");

        b.HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(e => !e.IsDeleted);

        b.HasIndex(e => new { e.StaffId, e.Year, e.Month })
         .HasDatabaseName("IX_HostessExpenses_StaffId_YearMonth");
    }
}
