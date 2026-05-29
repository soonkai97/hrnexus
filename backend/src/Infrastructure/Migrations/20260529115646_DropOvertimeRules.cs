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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
