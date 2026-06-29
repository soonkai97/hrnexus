using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkLogStartGeo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "StartLatitude",
                table: "WorkLogs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StartLongitude",
                table: "WorkLogs",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartLatitude",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "StartLongitude",
                table: "WorkLogs");
        }
    }
}
