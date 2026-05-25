using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations;

/// <inheritdoc />
public partial class CleanStaleFeatureFlags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            DELETE FROM ""FeatureFlags""
            WHERE ""Key"" IN ('shiftSwaps', 'availability', 'myShifts');
        ");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            INSERT INTO ""FeatureFlags"" (""Key"", ""IsEnabled"")
            VALUES ('shiftSwaps', false), ('availability', false), ('myShifts', false)
            ON CONFLICT (""Key"") DO NOTHING;
        ");
    }
}
