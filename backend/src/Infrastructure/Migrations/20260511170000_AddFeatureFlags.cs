using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NightclubWorkforce.Infrastructure.Persistence;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260511170000_AddFeatureFlags")]
    public partial class AddFeatureFlags : Migration
    {
        // All recognised feature keys — seeded as disabled by default.
        private static readonly string[] Keys =
        [
            "templates", "events", "payroll",
            "messagingAdmin", "availability", "tipsHistory", "messagingStaff"
        ];

        protected override void Up(MigrationBuilder mb)
        {
            mb.CreateTable(
                name: "FeatureFlags",
                columns: t => new
                {
                    Key       = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UpdatedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: t => t.PrimaryKey("PK_FeatureFlags", x => x.Key));

            // Seed all known keys as disabled; use ON CONFLICT DO NOTHING so
            // re-running the migration (e.g. in dev) is safe.
            foreach (var key in Keys)
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
            mb.DropTable("FeatureFlags");
        }
    }
}
