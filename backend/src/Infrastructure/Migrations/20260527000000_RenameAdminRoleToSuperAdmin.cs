using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameAdminRoleToSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
