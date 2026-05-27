using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRNexus.Infrastructure.Persistence;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527000001_SeedStaffLedgerFlag")]
    public partial class SeedStaffLedgerFlag : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            mb.Sql("""
                INSERT INTO "FeatureFlags" ("Key", "IsEnabled", "UpdatedAt")
                VALUES ('staffLedger', false, NOW() AT TIME ZONE 'UTC')
                ON CONFLICT ("Key") DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql("""DELETE FROM "FeatureFlags" WHERE "Key" = 'staffLedger';""");
        }
    }
}
