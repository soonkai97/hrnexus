using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRNexus.Infrastructure.Persistence;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260522000002_AddHostessLedger")]
    public partial class AddHostessLedger : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            // ── HostessEntries ────────────────────────────────────────────────────
            mb.Sql(@"
CREATE TABLE ""HostessEntries"" (
    ""Id""            uuid                         NOT NULL,
    ""StaffId""       uuid                         NOT NULL,
    ""EntryDate""     date                         NOT NULL,
    ""WorkType""      varchar(50)                  NOT NULL,
    ""VenueUnits""    numeric(10,2)                NOT NULL DEFAULT 0,
    ""VenueRate""     numeric(10,2)                NOT NULL DEFAULT 0,
    ""VenueEarnings"" numeric(10,2)                NOT NULL DEFAULT 0,
    ""PrivateGross""  numeric(10,2)                NOT NULL DEFAULT 0,
    ""PrivateNet""    numeric(10,2)                NOT NULL DEFAULT 0,
    ""Settlement""    varchar(50)                  NOT NULL DEFAULT 'PENDING',
    ""Notes""         varchar(500),
    ""IsDeleted""     boolean                      NOT NULL DEFAULT false,
    ""CreatedAt""     timestamp with time zone     NOT NULL,
    ""UpdatedAt""     timestamp with time zone     NOT NULL,
    CONSTRAINT ""PK_HostessEntries"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_HostessEntries_Staff"" FOREIGN KEY (""StaffId"") REFERENCES ""Staff""(""Id"") ON DELETE RESTRICT
);
CREATE INDEX ""IX_HostessEntries_StaffId_EntryDate"" ON ""HostessEntries"" (""StaffId"", ""EntryDate"");
");

            // ── HostessExpenses ───────────────────────────────────────────────────
            mb.Sql(@"
CREATE TABLE ""HostessExpenses"" (
    ""Id""        uuid                         NOT NULL,
    ""StaffId""   uuid                         NOT NULL,
    ""Year""      integer                      NOT NULL,
    ""Month""     integer                      NOT NULL,
    ""Category""  varchar(100)                 NOT NULL,
    ""Amount""    numeric(10,2)                NOT NULL DEFAULT 0,
    ""Notes""     varchar(500),
    ""IsDeleted"" boolean                      NOT NULL DEFAULT false,
    ""CreatedAt"" timestamp with time zone     NOT NULL,
    ""UpdatedAt"" timestamp with time zone     NOT NULL,
    CONSTRAINT ""PK_HostessExpenses"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_HostessExpenses_Staff"" FOREIGN KEY (""StaffId"") REFERENCES ""Staff""(""Id"") ON DELETE RESTRICT
);
CREATE INDEX ""IX_HostessExpenses_StaffId_YearMonth"" ON ""HostessExpenses"" (""StaffId"", ""Year"", ""Month"");
");

            // ── HostessLoans ──────────────────────────────────────────────────────
            mb.Sql(@"
CREATE TABLE ""HostessLoans"" (
    ""Id""        uuid                         NOT NULL,
    ""StaffId""   uuid                         NOT NULL,
    ""LoanDate""  date                         NOT NULL,
    ""LoanType""  varchar(20)                  NOT NULL,
    ""Amount""    numeric(10,2)                NOT NULL DEFAULT 0,
    ""Purpose""   varchar(300),
    ""IsDeleted"" boolean                      NOT NULL DEFAULT false,
    ""CreatedAt"" timestamp with time zone     NOT NULL,
    CONSTRAINT ""PK_HostessLoans"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_HostessLoans_Staff"" FOREIGN KEY (""StaffId"") REFERENCES ""Staff""(""Id"") ON DELETE RESTRICT
);
CREATE INDEX ""IX_HostessLoans_StaffId_LoanDate"" ON ""HostessLoans"" (""StaffId"", ""LoanDate"");
");

            // ── Feature flag seed ─────────────────────────────────────────────────
            mb.Sql(@"
INSERT INTO ""FeatureFlags"" (""Key"", ""IsEnabled"", ""UpdatedAt"")
VALUES ('hostessLedger', false, now())
ON CONFLICT (""Key"") DO NOTHING;
");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql(@"DELETE FROM ""FeatureFlags"" WHERE ""Key"" = 'hostessLedger';");
            mb.Sql(@"DROP TABLE IF EXISTS ""HostessLoans"";");
            mb.Sql(@"DROP TABLE IF EXISTS ""HostessExpenses"";");
            mb.Sql(@"DROP TABLE IF EXISTS ""HostessEntries"";");
        }
    }
}
