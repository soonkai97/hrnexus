using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing shifts + roles predate the pay-type feature → default them to "hourly"
            // (the only pricing behaviour the app had before this migration).
            migrationBuilder.AddColumn<string>(
                name: "PayTypeSnapshot",
                table: "WorkLogs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "hourly");

            migrationBuilder.AddColumn<string>(
                name: "PayType",
                table: "Roles",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "hourly");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayTypeSnapshot",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "PayType",
                table: "Roles");
        }
    }
}
