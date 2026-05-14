using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations;

/// <summary>
/// Removes 'events' and 'templates' feature flags that have no corresponding
/// backend implementation or frontend UI.
/// </summary>
public partial class DropOrphanFeatureFlags : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            DELETE FROM ""FeatureFlags""
            WHERE ""Key"" IN ('events', 'templates');
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            INSERT INTO ""FeatureFlags"" (""Key"", ""IsEnabled"", ""UpdatedAt"")
            VALUES ('events', false, NOW()), ('templates', false, NOW())
            ON CONFLICT (""Key"") DO NOTHING;
        ");
    }
}
