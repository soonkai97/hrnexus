using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence.Configurations;

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
        b.Property(e => e.Email).HasMaxLength(200).IsRequired();
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

        b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.ModifiedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.ModifiedBy).HasMaxLength(100);
        b.Property(e => e.DeletedAt).HasColumnType("timestamp with time zone");
        b.Property(e => e.DeletedBy).HasMaxLength(100);

        b.HasQueryFilter(e => !e.IsDeleted);

        // Computed properties — not mapped to columns.
        b.Ignore(e => e.Roles);
        b.Ignore(e => e.RolesDisplay);
        b.Ignore(e => e.FullName);
        b.Ignore(e => e.Age);
        b.Ignore(e => e.PermissionLevel);
    }
}
