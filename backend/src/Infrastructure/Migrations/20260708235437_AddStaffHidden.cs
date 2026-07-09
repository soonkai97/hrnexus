using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffHidden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Staff",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Hide the seeded bootstrap accounts from the roster by default. They stay fully functional;
            // an admin can unhide them from the staff list. StaffId is stored uppercase.
            migrationBuilder.Sql(
                "UPDATE \"Staff\" SET \"IsHidden\" = true WHERE \"StaffId\" IN ('HRADMIN', 'HRMANAGER');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Staff");
        }
    }
}
