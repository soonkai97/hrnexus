using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NightclubWorkforce.Infrastructure.Persistence;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260511180000_SeedMoreFeatureFlags")]
    public partial class SeedMoreFeatureFlags : Migration
    {
        private static readonly string[] NewKeys =
        [
            "shiftSwaps", "announcements", "tips", "otRules"
        ];

        protected override void Up(MigrationBuilder mb)
        {
            foreach (var key in NewKeys)
            {
                mb.Sql($"""
                    INSERT INTO "FeatureFlags" ("Key", "IsEnabled", "UpdatedAt")
                    VALUES ('{key}', false, NOW() AT TIME ZONE 'UTC')
                    ON CONFLICT ("Key") DO NOTHING;
                    """);
            }
        }

        protected override void Down(MigrationBuilder mb)
        {
            foreach (var key in NewKeys)
            {
                mb.Sql($"""DELETE FROM "FeatureFlags" WHERE "Key" = '{key}';""");
            }
        }
    }
}
