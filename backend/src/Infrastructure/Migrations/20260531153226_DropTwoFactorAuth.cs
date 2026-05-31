using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropTwoFactorAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "TwoFactorRecoveryCodes",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "TwoFactorSecret",
                table: "Staff");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "Staff",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorRecoveryCodes",
                table: "Staff",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSecret",
                table: "Staff",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
