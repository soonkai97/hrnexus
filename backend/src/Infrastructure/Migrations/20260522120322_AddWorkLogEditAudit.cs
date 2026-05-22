using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkLogEditAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "WorkLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "WorkLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "IsEdited",
                table: "WorkLogs");
        }
    }
}
