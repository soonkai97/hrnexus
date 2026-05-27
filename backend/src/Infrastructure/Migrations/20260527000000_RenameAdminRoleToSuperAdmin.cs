using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRNexus.Infrastructure.Persistence;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527000000_RenameAdminRoleToSuperAdmin")]
    public partial class RenameAdminRoleToSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SeedData may have already inserted a 'SUPERADMIN' row on a previous startup
            // before this migration was discovered. Remove it so the UPDATE below doesn't
            // violate the PK_Roles unique constraint.
            migrationBuilder.Sql("""
                DELETE FROM "Roles" WHERE "Name" = 'SUPERADMIN';
                """);

            // Rename the system role record itself
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Name" = 'SUPERADMIN' WHERE "Name" = 'ADMIN';
                """);

            // Rename all staff role assignments
            migrationBuilder.Sql("""
                UPDATE "StaffRoles" SET "Role" = 'SUPERADMIN' WHERE "Role" = 'ADMIN';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Name" = 'ADMIN' WHERE "Name" = 'SUPERADMIN';
                """);

            migrationBuilder.Sql("""
                UPDATE "StaffRoles" SET "Role" = 'SUPERADMIN' WHERE "Role" = 'ADMIN';
                """);
        }
    }
}
