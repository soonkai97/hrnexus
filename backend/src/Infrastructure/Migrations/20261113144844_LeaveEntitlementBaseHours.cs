using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaveEntitlementBaseHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddCarryOver used to add the carried hours into AnnualHours as well as CarryOverHours, so an
            // entitlement edit overwrote them. AnnualHours is now the base only; the usable pool is
            // AnnualHours + CarryOverHours. Move the carried hours out of the base value.
            migrationBuilder.Sql(@"
                UPDATE ""LeaveEntitlements""
                SET ""AnnualHours"" = GREATEST(""AnnualHours"" - ""CarryOverHours"", 0)
                WHERE ""CarryOverHours"" > 0;
            ");
        }

        /// <inheritdoc />
        // Lossy: a base smaller than its carry-over was clamped to 0 by Up, and rows whose base was edited
        // after a carry-over (so the carry-over was no longer inside it) are over-counted here.
        // Acceptable while no production data exists; take a backup before reverting on real data.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""LeaveEntitlements""
                SET ""AnnualHours"" = ""AnnualHours"" + ""CarryOverHours""
                WHERE ""CarryOverHours"" > 0;
            ");
        }
    }
}
