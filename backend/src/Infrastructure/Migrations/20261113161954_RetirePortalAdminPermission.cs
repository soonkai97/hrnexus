using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetirePortalAdminPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // There is one web app now: any active account can sign in and permissions alone decide what a
            // person may do. The 'portal.admin' key no longer exists, so remove its grants and overrides.
            migrationBuilder.Sql(@"
                DELETE FROM ""RolePermissions"" WHERE ""PermissionKey"" = 'portal.admin';
                DELETE FROM ""StaffPermissionOverrides"" WHERE ""PermissionKey"" = 'portal.admin';
            ");
        }

        /// <inheritdoc />
        // Not restorable: who held 'portal.admin' is not recorded. Re-grant it from the roles page if needed.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
