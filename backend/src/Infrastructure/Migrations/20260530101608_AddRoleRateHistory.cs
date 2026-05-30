using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleRateHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleRates_RoleName_EffectiveOn",
                table: "RoleRates",
                columns: new[] { "RoleName", "EffectiveOn" });

            // Backfill one baseline rate row per existing role at a floor date so every past (and
            // back-dated) shift resolves to the role's current rate — matching prior behaviour.
            // From here on, each rate change appends a dated row, so history diverges correctly.
            migrationBuilder.Sql(@"
                INSERT INTO ""RoleRates"" (""Id"", ""RoleName"", ""Rate"", ""EffectiveOn"", ""CreatedAt"", ""CreatedBy"")
                SELECT gen_random_uuid(), ""Name"", ""HourlyRate"", DATE '2000-01-01', now() AT TIME ZONE 'utc', 'system-backfill'
                FROM ""Roles"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleRates");
        }
    }
}
