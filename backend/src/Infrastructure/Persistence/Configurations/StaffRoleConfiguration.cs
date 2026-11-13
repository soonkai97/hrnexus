using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRNexus.Infrastructure.Persistence.Configurations;

public class StaffRoleConfiguration : IEntityTypeConfiguration<StaffRole>
{
    public void Configure(EntityTypeBuilder<StaffRole> b)
    {
        b.ToTable("StaffRoles");
        b.HasKey(r => new { r.EmployeeId, r.RoleId });
        // Role navigation is AutoInclude()d so name-based auth (staffRole.Role.Name) keeps working
        // after the switch from a name key to a GUID key. Restrict delete: a role that is still
        // assigned to staff cannot be removed (DeleteRoleCommand guards this with a friendly error).
        b.HasOne(r => r.Role)
            .WithMany()
            .HasForeignKey(r => r.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Navigation(r => r.Role).AutoInclude();
    }
}
