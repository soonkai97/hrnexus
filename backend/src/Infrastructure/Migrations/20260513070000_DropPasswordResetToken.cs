using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DropPasswordResetToken : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PasswordResetToken",
            table: "Staff");

        migrationBuilder.DropColumn(
            name: "PasswordResetTokenExpiry",
            table: "Staff");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PasswordResetToken",
            table: "Staff",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PasswordResetTokenExpiry",
            table: "Staff",
            type: "timestamp with time zone",
            nullable: true);
    }
}
