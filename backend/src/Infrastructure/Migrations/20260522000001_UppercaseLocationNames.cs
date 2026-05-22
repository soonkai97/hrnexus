using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NightclubWorkforce.Infrastructure.Persistence;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260522000001_UppercaseLocationNames")]
    public partial class UppercaseLocationNames : Migration
    {
        /// <summary>
        /// Converts all location names to UPPER CASE and adds a partial unique index
        /// so that duplicate active location names are rejected at the DB level.
        ///
        ///   Locations.Name     — display name; used in QR clock-in lookups
        ///   WorkLogs.LocationName — denormalised snapshot stored at shift-log time
        ///
        /// The unique index is partial (WHERE "IsDeleted" = false) so that a soft-deleted
        /// location name can be reused by a new location without a constraint violation.
        /// </summary>
        protected override void Up(MigrationBuilder mb)
        {
            // 1. Uppercase existing location names
            mb.Sql(@"UPDATE ""Locations"" SET ""Name"" = UPPER(""Name"")");

            // 2. Uppercase WorkLog location-name snapshots (column is NOT NULL)
            mb.Sql(@"UPDATE ""WorkLogs"" SET ""LocationName"" = UPPER(""LocationName"")");

            // 3. Partial unique index — enforces no duplicate active location names
            mb.Sql(@"CREATE UNIQUE INDEX ""IX_Locations_Name_Active"" ON ""Locations"" (""Name"") WHERE ""IsDeleted"" = false");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql(@"DROP INDEX ""IX_Locations_Name_Active""");
            mb.Sql(@"UPDATE ""Locations"" SET ""Name"" = LOWER(""Name"")");
            mb.Sql(@"UPDATE ""WorkLogs"" SET ""LocationName"" = LOWER(""LocationName"")");
        }
    }
}
