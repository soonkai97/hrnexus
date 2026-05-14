using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropShiftTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftTemplateEntries");

            migrationBuilder.DropTable(
                name: "ShiftTemplates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTemplateEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EndTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    LocationId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTemplateEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTemplateEntries_ShiftTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplateEntries_TemplateId",
                table: "ShiftTemplateEntries",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_IsActive",
                table: "ShiftTemplates",
                column: "IsActive");
        }
    }
}
