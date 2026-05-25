using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaveHourBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── LeaveRequests: add Hours, StartTime, EndTime columns ──────────────
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN IF NOT EXISTS ""Hours"" numeric(8,2);");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN IF NOT EXISTS ""StartTime"" character varying(5);");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN IF NOT EXISTS ""EndTime"" character varying(5);");

            // ── LeaveEntitlements: rename Days→Hours, change int→decimal, reset to 0 ──
            // Rename AnnualDays → AnnualHours
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" RENAME COLUMN ""AnnualDays"" TO ""AnnualHours"";");
            // Rename SickDays → SickHours
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" RENAME COLUMN ""SickDays"" TO ""SickHours"";");
            // Change type from integer to numeric(8,2)
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" ALTER COLUMN ""AnnualHours"" TYPE numeric(8,2) USING ""AnnualHours""::numeric(8,2);");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" ALTER COLUMN ""SickHours"" TYPE numeric(8,2) USING ""SickHours""::numeric(8,2);");
            // Clean slate: reset all existing entitlements to 0 (no 8h/day assumption)
            migrationBuilder.Sql(@"UPDATE ""LeaveEntitlements"" SET ""AnnualHours"" = 0, ""SickHours"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" DROP COLUMN IF EXISTS ""Hours"";");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" DROP COLUMN IF EXISTS ""StartTime"";");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" DROP COLUMN IF EXISTS ""EndTime"";");

            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" ALTER COLUMN ""AnnualHours"" TYPE integer USING ""AnnualHours""::integer;");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" ALTER COLUMN ""SickHours"" TYPE integer USING ""SickHours""::integer;");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" RENAME COLUMN ""AnnualHours"" TO ""AnnualDays"";");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveEntitlements"" RENAME COLUMN ""SickHours"" TO ""SickDays"";");
        }
    }
}
