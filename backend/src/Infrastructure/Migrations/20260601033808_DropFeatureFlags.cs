using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureFlags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureFlags",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlags", x => x.Key);
                });
        }
    }
}
