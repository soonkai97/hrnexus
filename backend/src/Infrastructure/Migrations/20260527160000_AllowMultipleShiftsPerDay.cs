using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleShiftsPerDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the per-day unique constraint — multiple shifts per employee per day
            // are now allowed as long as they don't overlap in time. The application-level
            // overlap check in CreateManualWorkLogCommand / UpdateWorkLogCommand enforces this.
            migrationBuilder.DropIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate_Active",
                table: "WorkLogs");

            // Replace with a plain composite index for query performance on overlap checks.
            migrationBuilder.CreateIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate",
                table: "WorkLogs",
                columns: new[] { "EmployeeId", "WorkDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate",
                table: "WorkLogs");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate_Active",
                table: "WorkLogs",
                columns: new[] { "EmployeeId", "WorkDate" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
