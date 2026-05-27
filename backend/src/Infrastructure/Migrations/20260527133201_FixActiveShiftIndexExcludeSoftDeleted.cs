using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixActiveShiftIndexExcludeSoftDeleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogs_EmployeeId_Active",
                table: "WorkLogs");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogs_EmployeeId_Active",
                table: "WorkLogs",
                column: "EmployeeId",
                unique: true,
                filter: "\"EndTime\" IS NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogs_EmployeeId_Active",
                table: "WorkLogs");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogs_EmployeeId_Active",
                table: "WorkLogs",
                column: "EmployeeId",
                unique: true,
                filter: "\"EndTime\" IS NULL");
        }
    }
}
