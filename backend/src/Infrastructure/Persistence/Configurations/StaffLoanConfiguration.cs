using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffLoanConfiguration : IEntityTypeConfiguration<StaffLoan>
{
    public void Configure(EntityTypeBuilder<StaffLoan> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.LoanType).IsRequired().HasMaxLength(20);
        b.Property(e => e.Amount).HasColumnType("numeric(10,2)");
        b.Property(e => e.Purpose).HasMaxLength(300);
        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);
        b.Property(e => e.DeletedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.DeletedBy).HasMaxLength(100);

        b.HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(e => !e.IsDeleted);

        b.HasIndex(e => new { e.StaffId, e.LoanDate })
         .HasDatabaseName("IX_StaffLoans_StaffId_LoanDate");
    }
}
