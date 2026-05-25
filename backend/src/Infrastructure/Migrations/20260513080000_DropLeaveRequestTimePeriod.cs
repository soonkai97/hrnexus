using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DropLeaveRequestTimePeriod : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "StartTime",
            table: "LeaveRequests");

        migrationBuilder.DropColumn(
            name: "EndTime",
            table: "LeaveRequests");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StartTime",
            table: "LeaveRequests",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EndTime",
            table: "LeaveRequests",
            type: "text",
            nullable: true);
    }
}
