using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRedundantIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TipsAllocations_EmployeeId",
                table: "TipsAllocations");

            migrationBuilder.DropIndex(
                name: "IX_TipsAllocations_PoolId",
                table: "TipsAllocations");

            migrationBuilder.DropIndex(
                name: "IX_PushSubscriptions_EmployeeId",
                table: "PushSubscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TipsAllocations_EmployeeId",
                table: "TipsAllocations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TipsAllocations_PoolId",
                table: "TipsAllocations",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_EmployeeId",
                table: "PushSubscriptions",
                column: "EmployeeId");
        }
    }
}
