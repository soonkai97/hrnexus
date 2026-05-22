using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NightclubWorkforce.Infrastructure.Persistence;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260522000003_RemoveStaleFeatureFlags")]
    public partial class RemoveStaleFeatureFlags : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            mb.Sql(@"
DELETE FROM ""FeatureFlags""
WHERE ""Key"" IN ('messagingAdmin', 'messagingStaff', 'otRules');
");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql(@"
INSERT INTO ""FeatureFlags"" (""Key"", ""IsEnabled"", ""UpdatedAt"")
VALUES
  ('messagingAdmin', false, now()),
  ('messagingStaff', false, now()),
  ('otRules',        false, now())
ON CONFLICT (""Key"") DO NOTHING;
");
        }
    }
}
