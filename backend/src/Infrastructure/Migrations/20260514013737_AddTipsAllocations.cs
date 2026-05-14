using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTipsAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TipsAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    HoursWorked = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipsAllocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TipsAllocations_EmployeeId",
                table: "TipsAllocations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TipsAllocations_PoolId",
                table: "TipsAllocations",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "IX_TipsAllocations_PoolId_EmployeeId",
                table: "TipsAllocations",
                columns: new[] { "PoolId", "EmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TipsAllocations");
        }
    }
}
