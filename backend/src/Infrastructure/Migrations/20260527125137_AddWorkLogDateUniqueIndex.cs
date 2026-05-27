using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkLogDateUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate_Active",
                table: "WorkLogs",
                columns: new[] { "EmployeeId", "WorkDate" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate_Active",
                table: "WorkLogs");
        }
    }
}
