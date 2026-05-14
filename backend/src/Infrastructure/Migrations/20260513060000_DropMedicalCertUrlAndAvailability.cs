using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DropMedicalCertUrlAndAvailability : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Drop the StaffAvailabilities table (feature removed)
        migrationBuilder.DropTable(
            name: "StaffAvailabilities");

        // Drop the MedicalCertUrl column (MC upload feature removed)
        migrationBuilder.DropColumn(
            name: "MedicalCertUrl",
            table: "LeaveRequests");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore MedicalCertUrl column
        migrationBuilder.AddColumn<string>(
            name: "MedicalCertUrl",
            table: "LeaveRequests",
            type: "text",
            nullable: true);

        // Restore StaffAvailabilities table
        migrationBuilder.CreateTable(
            name: "StaffAvailabilities",
            columns: table => new
            {
                Id           = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId   = table.Column<Guid>(type: "uuid", nullable: false),
                DayOfWeek    = table.Column<int>(type: "integer", nullable: false),
                IsUnavailable = table.Column<bool>(type: "boolean", nullable: false),
                StartTime    = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                EndTime      = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                Note         = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                UpdatedAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StaffAvailabilities", x => x.Id);
                table.ForeignKey(
                    name: "FK_StaffAvailabilities_Staff_EmployeeId",
                    column: x => x.EmployeeId,
                    principalTable: "Staff",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StaffAvailabilities_EmployeeId",
            table: "StaffAvailabilities",
            column: "EmployeeId");
    }
}
