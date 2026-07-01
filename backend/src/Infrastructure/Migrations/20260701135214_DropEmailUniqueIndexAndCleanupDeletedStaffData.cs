using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropEmailUniqueIndexAndCleanupDeletedStaffData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Email carries no unique constraint any more — it's optional contact info, never used to
            // log in or recover a password (Staff ID is). Staff may share one address or have none.
            migrationBuilder.DropIndex(
                name: "IX_Staff_Email",
                table: "Staff");

            // One-time cleanup: remove records left orphaned behind staff that were deleted
            // (soft-deleted) BEFORE the delete flow started cleaning up all related data. WorkLogs are
            // intentionally left untouched (deleting staff with shift history is blocked, so soft-deleted
            // staff have none — and payroll history is never purged by this maintenance step).
            migrationBuilder.Sql(@"
                DELETE FROM ""StaffRoles""               WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
                DELETE FROM ""StaffLocations""           WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
                DELETE FROM ""LeaveRequests""            WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
                DELETE FROM ""LeaveEntitlements""        WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
                DELETE FROM ""CashAdvances""             WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
                DELETE FROM ""StaffPermissionOverrides"" WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
                DELETE FROM ""Notifications""            WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
                DELETE FROM ""PushSubscriptions""        WHERE ""EmployeeId"" IN (SELECT ""Id"" FROM ""Staff"" WHERE ""IsDeleted"" = true);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Staff_Email",
                table: "Staff",
                column: "Email",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
