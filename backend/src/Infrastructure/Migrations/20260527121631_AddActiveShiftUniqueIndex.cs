using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveShiftUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: xmin is a PostgreSQL system column (transaction ID) that already exists
            // on every table row.  EF Core is configured to use it as a concurrency token
            // (shadow property) so no DDL is needed — we just teach EF to SELECT and check it.
            //
            // NOTE: IX_WorkLogs_EmployeeId (the general employee index) is intentionally kept;
            // only the new partial unique index is added here.

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogs_EmployeeId_Active",
                table: "WorkLogs",
                column: "EmployeeId",
                unique: true,
                filter: "\"EndTime\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogs_EmployeeId_Active",
                table: "WorkLogs");
        }
    }
}
