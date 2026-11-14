using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaveTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Colour = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Pool = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AllowanceDays = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    MaxDaysPerRequest = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    AutoApprove = table.Column<bool>(type: "boolean", nullable: false),
                    AdvanceNoticeDays = table.Column<int>(type: "integer", nullable: true),
                    AdvanceNoticeFromPolicy = table.Column<bool>(type: "boolean", nullable: false),
                    DeductsPay = table.Column<bool>(type: "boolean", nullable: false),
                    AdminOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CountsCalendarDays = table.Column<bool>(type: "boolean", nullable: false),
                    Sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    CatalogVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_SortOrder",
                table: "LeaveTypes",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveTypes");
        }
    }
}
