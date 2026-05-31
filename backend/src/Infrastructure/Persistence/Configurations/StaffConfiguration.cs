using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> b)
    {
        b.ToTable("Staff");
        b.HasKey(e => e.Id);
        b.Property(e => e.StaffId).HasMaxLength(20).IsRequired();
        b.HasIndex(e => e.StaffId).IsUnique();
        b.Property(e => e.FirstName).HasMaxLength(50).IsRequired();
        b.Property(e => e.LastName).HasMaxLength(50).IsRequired();
        b.Property(e => e.Email).HasMaxLength(200);  // nullable — email is optional at registration
        b.HasIndex(e => e.Email).IsUnique();
        b.Property(e => e.PasswordHash).IsRequired();
        b.Property(e => e.IcNo).HasMaxLength(20);
        b.Property(e => e.PassportNo).HasMaxLength(30);

        // Roles are normalised into a child EmployeeRoles table (one-to-many).
        // AutoInclude means every LINQ query loads roles without an explicit Include().
        b.HasMany(e => e.RoleEntries)
            .WithOne()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(e => e.RoleEntries).AutoInclude();

        // RefreshToken as a concurrency token: EF includes it in the UPDATE WHERE clause.
        // If two concurrent 401s both read the same token, the second SaveChanges finds
        // 0 rows (token already rotated) and throws DbUpdateConcurrencyException.
        b.Property(e => e.RefreshToken).IsConcurrencyToken();

        // Two-factor auth: secret is a short base32 string; recovery codes are newline-joined hashes.
        b.Property(e => e.TwoFactorSecret).HasMaxLength(64);
        b.Property(e => e.TwoFactorRecoveryCodes);   // text — joined SHA-256 hex hashes

        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);
        b.Property(e => e.DeletedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.DeletedBy).HasMaxLength(100);

        b.HasQueryFilter(e => !e.IsDeleted);

        // Computed properties — not mapped to columns.
        b.Ignore(e => e.Roles);
        b.Ignore(e => e.FullName);
        b.Ignore(e => e.Age);
        b.Ignore(e => e.PermissionLevel);
    }
}
