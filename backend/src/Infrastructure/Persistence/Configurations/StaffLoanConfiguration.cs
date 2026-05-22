using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

public class StaffLoanConfiguration : IEntityTypeConfiguration<StaffLoan>
{
    public void Configure(EntityTypeBuilder<StaffLoan> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.LoanType).IsRequired().HasMaxLength(20);
        b.Property(e => e.Amount).HasColumnType("numeric(10,2)");
        b.Property(e => e.Purpose).HasMaxLength(300);
        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");

        b.HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(e => !e.IsDeleted);

        b.HasIndex(e => new { e.StaffId, e.LoanDate })
         .HasDatabaseName("IX_StaffLoans_StaffId_LoanDate");
    }
}
