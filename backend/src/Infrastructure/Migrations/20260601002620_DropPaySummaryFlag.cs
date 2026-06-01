using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropPaySummaryFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Staff "Pay Summary" feature removed entirely. Drop its (disabled) feature-flag row.
            // The feature had no dedicated tables or permission grants — just this flag row.
            migrationBuilder.Sql(@"DELETE FROM ""FeatureFlags"" WHERE ""Key"" = 'paySummary';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-seed the (disabled) flag so a rollback restores the prior row.
            migrationBuilder.Sql(@"
                INSERT INTO ""FeatureFlags"" (""Key"", ""IsEnabled"", ""UpdatedAt"")
                VALUES ('paySummary', false, NOW() AT TIME ZONE 'UTC')
                ON CONFLICT (""Key"") DO NOTHING;");
        }
    }
}
