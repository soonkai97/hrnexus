using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NightclubWorkforce.Infrastructure.Persistence;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260522000005_RenameHostessToStaff")]
    public partial class RenameHostessToStaff : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            // ── Rename tables ─────────────────────────────────────────────────────
            mb.Sql(@"ALTER TABLE ""HostessEntries""    RENAME TO ""StaffEntries"";");
            mb.Sql(@"ALTER TABLE ""HostessExpenses""   RENAME TO ""StaffExpenses"";");
            mb.Sql(@"ALTER TABLE ""HostessLoans""      RENAME TO ""StaffLoans"";");
            mb.Sql(@"ALTER TABLE ""HostessWorkTypes""  RENAME TO ""StaffWorkTypes"";");
            mb.Sql(@"ALTER TABLE ""HostessSettlements"" RENAME TO ""StaffSettlements"";");

            // ── Rename primary key constraints ────────────────────────────────────
            mb.Sql(@"ALTER TABLE ""StaffEntries""     RENAME CONSTRAINT ""PK_HostessEntries""     TO ""PK_StaffEntries"";");
            mb.Sql(@"ALTER TABLE ""StaffExpenses""    RENAME CONSTRAINT ""PK_HostessExpenses""    TO ""PK_StaffExpenses"";");
            mb.Sql(@"ALTER TABLE ""StaffLoans""       RENAME CONSTRAINT ""PK_HostessLoans""       TO ""PK_StaffLoans"";");
            mb.Sql(@"ALTER TABLE ""StaffWorkTypes""   RENAME CONSTRAINT ""PK_HostessWorkTypes""   TO ""PK_StaffWorkTypes"";");
            mb.Sql(@"ALTER TABLE ""StaffSettlements"" RENAME CONSTRAINT ""PK_HostessSettlements"" TO ""PK_StaffSettlements"";");

            // ── Rename foreign key constraints ────────────────────────────────────
            mb.Sql(@"ALTER TABLE ""StaffEntries""  RENAME CONSTRAINT ""FK_HostessEntries_Staff""  TO ""FK_StaffEntries_Staff"";");
            mb.Sql(@"ALTER TABLE ""StaffExpenses"" RENAME CONSTRAINT ""FK_HostessExpenses_Staff"" TO ""FK_StaffExpenses_Staff"";");
            mb.Sql(@"ALTER TABLE ""StaffLoans""    RENAME CONSTRAINT ""FK_HostessLoans_Staff""    TO ""FK_StaffLoans_Staff"";");

            // ── Rename unique index on StaffSettlements ───────────────────────────
            mb.Sql(@"ALTER INDEX ""IX_HostessSettlements_Key"" RENAME TO ""IX_StaffSettlements_Key"";");
            mb.Sql(@"ALTER INDEX ""IX_HostessWorkTypes_Key""   RENAME TO ""IX_StaffWorkTypes_Key"";");

            // ── Rename non-unique indexes ─────────────────────────────────────────
            mb.Sql(@"ALTER INDEX ""IX_HostessEntries_StaffId_EntryDate""    RENAME TO ""IX_StaffEntries_StaffId_EntryDate"";");
            mb.Sql(@"ALTER INDEX ""IX_HostessExpenses_StaffId_YearMonth""   RENAME TO ""IX_StaffExpenses_StaffId_YearMonth"";");
            mb.Sql(@"ALTER INDEX ""IX_HostessLoans_StaffId_LoanDate""       RENAME TO ""IX_StaffLoans_StaffId_LoanDate"";");

            // ── Rename feature flag key ───────────────────────────────────────────
            mb.Sql(@"UPDATE ""FeatureFlags"" SET ""Key"" = 'staffLedger' WHERE ""Key"" = 'hostessLedger';");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql(@"UPDATE ""FeatureFlags"" SET ""Key"" = 'hostessLedger' WHERE ""Key"" = 'staffLedger';");

            mb.Sql(@"ALTER INDEX ""IX_StaffEntries_StaffId_EntryDate""    RENAME TO ""IX_HostessEntries_StaffId_EntryDate"";");
            mb.Sql(@"ALTER INDEX ""IX_StaffExpenses_StaffId_YearMonth""   RENAME TO ""IX_HostessExpenses_StaffId_YearMonth"";");
            mb.Sql(@"ALTER INDEX ""IX_StaffLoans_StaffId_LoanDate""       RENAME TO ""IX_HostessLoans_StaffId_LoanDate"";");

            mb.Sql(@"ALTER INDEX ""IX_StaffSettlements_Key"" RENAME TO ""IX_HostessSettlements_Key"";");
            mb.Sql(@"ALTER INDEX ""IX_StaffWorkTypes_Key""   RENAME TO ""IX_HostessWorkTypes_Key"";");

            mb.Sql(@"ALTER TABLE ""StaffEntries""  RENAME CONSTRAINT ""FK_StaffEntries_Staff""  TO ""FK_HostessEntries_Staff"";");
            mb.Sql(@"ALTER TABLE ""StaffExpenses"" RENAME CONSTRAINT ""FK_StaffExpenses_Staff"" TO ""FK_HostessExpenses_Staff"";");
            mb.Sql(@"ALTER TABLE ""StaffLoans""    RENAME CONSTRAINT ""FK_StaffLoans_Staff""    TO ""FK_HostessLoans_Staff"";");

            mb.Sql(@"ALTER TABLE ""StaffEntries""     RENAME CONSTRAINT ""PK_StaffEntries""     TO ""PK_HostessEntries"";");
            mb.Sql(@"ALTER TABLE ""StaffExpenses""    RENAME CONSTRAINT ""PK_StaffExpenses""    TO ""PK_HostessExpenses"";");
            mb.Sql(@"ALTER TABLE ""StaffLoans""       RENAME CONSTRAINT ""PK_StaffLoans""       TO ""PK_HostessLoans"";");
            mb.Sql(@"ALTER TABLE ""StaffWorkTypes""   RENAME CONSTRAINT ""PK_StaffWorkTypes""   TO ""PK_HostessWorkTypes"";");
            mb.Sql(@"ALTER TABLE ""StaffSettlements"" RENAME CONSTRAINT ""PK_StaffSettlements"" TO ""PK_HostessSettlements"";");

            mb.Sql(@"ALTER TABLE ""StaffEntries""     RENAME TO ""HostessEntries"";");
            mb.Sql(@"ALTER TABLE ""StaffExpenses""    RENAME TO ""HostessExpenses"";");
            mb.Sql(@"ALTER TABLE ""StaffLoans""       RENAME TO ""HostessLoans"";");
            mb.Sql(@"ALTER TABLE ""StaffWorkTypes""   RENAME TO ""HostessWorkTypes"";");
            mb.Sql(@"ALTER TABLE ""StaffSettlements"" RENAME TO ""HostessSettlements"";");
        }
    }
}
