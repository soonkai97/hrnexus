using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NightclubWorkforce.Infrastructure.Persistence;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260522000004_AddHostessConfig")]
    public partial class AddHostessConfig : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            // ── HostessWorkTypes ──────────────────────────────────────────────────
            mb.Sql(@"
CREATE TABLE ""HostessWorkTypes"" (
    ""Id""           uuid         NOT NULL,
    ""Key""          varchar(50)  NOT NULL,
    ""Label""        varchar(100) NOT NULL,
    ""AutoCalc""     boolean      NOT NULL DEFAULT false,
    ""HasPrivate""   boolean      NOT NULL DEFAULT false,
    ""DisplayOrder"" integer      NOT NULL DEFAULT 0,
    ""IsActive""     boolean      NOT NULL DEFAULT true,
    CONSTRAINT ""PK_HostessWorkTypes"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX ""IX_HostessWorkTypes_Key"" ON ""HostessWorkTypes"" (""Key"");
");

            // ── HostessSettlements ────────────────────────────────────────────────
            mb.Sql(@"
CREATE TABLE ""HostessSettlements"" (
    ""Id""           uuid         NOT NULL,
    ""Key""          varchar(50)  NOT NULL,
    ""Label""        varchar(100) NOT NULL,
    ""Severity""     varchar(20)  NOT NULL DEFAULT 'secondary',
    ""DisplayOrder"" integer      NOT NULL DEFAULT 0,
    ""IsActive""     boolean      NOT NULL DEFAULT true,
    CONSTRAINT ""PK_HostessSettlements"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX ""IX_HostessSettlements_Key"" ON ""HostessSettlements"" (""Key"");
");

            // ── Seed work types ───────────────────────────────────────────────────
            mb.Sql(@"
INSERT INTO ""HostessWorkTypes"" (""Id"", ""Key"", ""Label"", ""AutoCalc"", ""HasPrivate"", ""DisplayOrder"", ""IsActive"") VALUES
  (gen_random_uuid(), 'VENUECLOCK',   '写钟',   true,  false, 1, true),
  (gen_random_uuid(), 'TABLERUN',     '跑台',   true,  false, 2, true),
  (gen_random_uuid(), 'OVERTIME',     '加时',   false, false, 3, true),
  (gen_random_uuid(), 'OVERNIGHT',    '包夜',   false, true,  4, true),
  (gen_random_uuid(), 'PRIVATEHOURS', '私小时', true,  true,  5, true),
  (gen_random_uuid(), 'SELFDEDUCT',   '自扣',   false, false, 6, true),
  (gen_random_uuid(), 'REST',         '休息',   false, false, 7, true),
  (gen_random_uuid(), 'SLOWNIGHT',    '吃蛋',   false, false, 8, true);
");

            // ── Seed settlements ──────────────────────────────────────────────────
            mb.Sql(@"
INSERT INTO ""HostessSettlements"" (""Id"", ""Key"", ""Label"", ""Severity"", ""DisplayOrder"", ""IsActive"") VALUES
  (gen_random_uuid(), 'PENDING',      'Pending',       'warn',      1, true),
  (gen_random_uuid(), 'SETTLED',      '清 (Settled)',  'success',   2, true),
  (gen_random_uuid(), 'CASH',         'Cash',          'success',   3, true),
  (gen_random_uuid(), 'BANKIN',       'Bank In',       'info',      4, true),
  (gen_random_uuid(), 'NOTCOLLECTED', '还没收',        'danger',    5, true),
  (gen_random_uuid(), 'COMP',         'Comp',          'secondary', 6, true);
");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql(@"DROP TABLE IF EXISTS ""HostessWorkTypes"";");
            mb.Sql(@"DROP TABLE IF EXISTS ""HostessSettlements"";");
        }
    }
}
