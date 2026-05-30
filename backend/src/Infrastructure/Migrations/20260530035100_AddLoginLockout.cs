using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the auto-generated "AddColumn xmin on WorkLogs" was removed by hand. xmin is a
            // PostgreSQL SYSTEM column that already exists on every row; "ALTER TABLE ... ADD COLUMN
            // xmin" errors with "conflicts with a system column name". The WorkLog xmin concurrency
            // token was mapped in config without a migration (same as LeaveRequest); this migration
            // is the first `migrations add` since, so it picked up the stale snapshot diff. The model
            // snapshot/Designer keeps the xmin mapping so future migrations won't regenerate this.

            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "Staff",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEnd",
                table: "Staff",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // xmin is a system column — no DropColumn (see Up()).
            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                table: "Staff");
        }
    }
}
