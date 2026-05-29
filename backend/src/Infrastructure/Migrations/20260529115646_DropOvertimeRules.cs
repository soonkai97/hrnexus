using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropOvertimeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove the overtime-rules feature entirely.
            migrationBuilder.DropTable(
                name: "OvertimeRules");

            // Drop the now-orphaned 'otRules' feature flag (nothing references it anymore).
            migrationBuilder.Sql("DELETE FROM \"FeatureFlags\" WHERE \"Key\" = 'otRules';");

            // Align the WorkLogs (EmployeeId, WorkDate) index with the current model: it is
            // intentionally NON-unique (multiple shifts per day are allowed, enforced by an
            // in-code time-overlap check). The DB still carried the older UNIQUE variant,
            // which would otherwise block a legitimate second same-day shift.
            migrationBuilder.DropIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate_Active",
                table: "WorkLogs");

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

            migrationBuilder.CreateTable(
                name: "OvertimeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Multiplier = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ThresholdHours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeRules", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "OvertimeRules",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Multiplier", "Role", "ThresholdHours" },
                values: new object[] { new Guid("11111111-0000-0000-0000-000000000001"), new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, 1.5m, "*", 8m });

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogs_EmployeeId_WorkDate_Active",
                table: "WorkLogs",
                columns: new[] { "EmployeeId", "WorkDate" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
