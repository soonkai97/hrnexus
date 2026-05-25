using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameEmployeeTablesToStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClockLogs_Employees_EmployeeId",
                table: "ClockLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeRoles_Employees_EmployeeId",
                table: "EmployeeRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveEntitlements_Employees_EmployeeId",
                table: "LeaveEntitlements");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequests_Employees_EmployeeId",
                table: "LeaveRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Employees_EmployeeId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_Employees_EmployeeId",
                table: "RosterEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Employees",
                table: "Employees");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EmployeeRoles",
                table: "EmployeeRoles");

            migrationBuilder.RenameTable(
                name: "Employees",
                newName: "Staff");

            migrationBuilder.RenameTable(
                name: "EmployeeRoles",
                newName: "StaffRoles");

            migrationBuilder.RenameIndex(
                name: "IX_Employees_StaffId",
                table: "Staff",
                newName: "IX_Staff_StaffId");

            migrationBuilder.RenameIndex(
                name: "IX_Employees_Email",
                table: "Staff",
                newName: "IX_Staff_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Staff",
                table: "Staff",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StaffRoles",
                table: "StaffRoles",
                columns: new[] { "EmployeeId", "Role" });

            migrationBuilder.AddForeignKey(
                name: "FK_ClockLogs_Staff_EmployeeId",
                table: "ClockLogs",
                column: "EmployeeId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveEntitlements_Staff_EmployeeId",
                table: "LeaveEntitlements",
                column: "EmployeeId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequests_Staff_EmployeeId",
                table: "LeaveRequests",
                column: "EmployeeId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Staff_EmployeeId",
                table: "Notifications",
                column: "EmployeeId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_Staff_EmployeeId",
                table: "RosterEntries",
                column: "EmployeeId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffRoles_Staff_EmployeeId",
                table: "StaffRoles",
                column: "EmployeeId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClockLogs_Staff_EmployeeId",
                table: "ClockLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveEntitlements_Staff_EmployeeId",
                table: "LeaveEntitlements");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequests_Staff_EmployeeId",
                table: "LeaveRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Staff_EmployeeId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_Staff_EmployeeId",
                table: "RosterEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffRoles_Staff_EmployeeId",
                table: "StaffRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StaffRoles",
                table: "StaffRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Staff",
                table: "Staff");

            migrationBuilder.RenameTable(
                name: "StaffRoles",
                newName: "EmployeeRoles");

            migrationBuilder.RenameTable(
                name: "Staff",
                newName: "Employees");

            migrationBuilder.RenameIndex(
                name: "IX_Staff_StaffId",
                table: "Employees",
                newName: "IX_Employees_StaffId");

            migrationBuilder.RenameIndex(
                name: "IX_Staff_Email",
                table: "Employees",
                newName: "IX_Employees_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EmployeeRoles",
                table: "EmployeeRoles",
                columns: new[] { "EmployeeId", "Role" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Employees",
                table: "Employees",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ClockLogs_Employees_EmployeeId",
                table: "ClockLogs",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeRoles_Employees_EmployeeId",
                table: "EmployeeRoles",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveEntitlements_Employees_EmployeeId",
                table: "LeaveEntitlements",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequests_Employees_EmployeeId",
                table: "LeaveRequests",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Employees_EmployeeId",
                table: "Notifications",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_Employees_EmployeeId",
                table: "RosterEntries",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
