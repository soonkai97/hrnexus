using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRNexus.Infrastructure.Persistence;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260512000000_WorkLogAndRoleRate")]
    public partial class WorkLogAndRoleRate : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            // ── Drop obsolete tables ────────────────────────────────────────
            // IncidentRecords and VenueEvents were added in AddNewFeatures (20260511160000)
            mb.DropTable("IncidentRecords");
            mb.DropTable("VenueEvents");

            // ShiftSwapRequests references RosterEntries — must drop first
            mb.DropTable("ShiftSwapRequests");

            // RosterEntries was in original schema
            mb.DropTable("RosterEntries");

            // QrCodes was in original schema
            mb.DropTable("QrCodes");

            // ShiftTemplates/ShiftTemplateEntries were already dropped in DropShiftTemplates (20260511105628)
            // so we do NOT drop them again here.

            // ── Drop columns from Staff ─────────────────────────────────────
            mb.DropColumn("HourlyRate",          "Staff");
            mb.DropColumn("IcDocumentUrl",        "Staff");
            mb.DropColumn("PassportDocumentUrl",  "Staff");
            mb.DropColumn("MagicToken",           "Staff");
            mb.DropColumn("MagicTokenExpiry",     "Staff");

            // ── Add HourlyRate to Roles ─────────────────────────────────────
            mb.AddColumn<decimal>(
                name:          "HourlyRate",
                table:         "Roles",
                type:          "numeric(10,2)",
                nullable:      false,
                defaultValue:  0m);

            // ── Create WorkLogs table ───────────────────────────────────────
            mb.CreateTable(
                name: "WorkLogs",
                columns: table => new
                {
                    Id                 = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId         = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleName           = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LocationId         = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LocationName       = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkDate           = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime          = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    EndTime            = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    IsManualEntry      = table.Column<bool>(type: "boolean", nullable: false),
                    Notes              = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HourlyRateSnapshot = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    CreatedAt          = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLogs", x => x.Id);
                    table.ForeignKey(
                        name:             "FK_WorkLogs_Staff_EmployeeId",
                        column:           x => x.EmployeeId,
                        principalTable:   "Staff",
                        principalColumn:  "Id",
                        onDelete:         ReferentialAction.Cascade);
                });

            mb.CreateIndex("IX_WorkLogs_EmployeeId", "WorkLogs", "EmployeeId");
            mb.CreateIndex("IX_WorkLogs_WorkDate",   "WorkLogs", "WorkDate");
        }

        protected override void Down(MigrationBuilder mb)
        {
            // Drop WorkLogs
            mb.DropTable("WorkLogs");

            // Remove HourlyRate from Roles
            mb.DropColumn("HourlyRate", "Roles");

            // Restore Staff columns
            mb.AddColumn<decimal>(
                name: "HourlyRate", table: "Staff",
                type: "numeric(10,2)", nullable: false, defaultValue: 0m);
            mb.AddColumn<string>(
                name: "IcDocumentUrl", table: "Staff",
                type: "text", nullable: true);
            mb.AddColumn<string>(
                name: "PassportDocumentUrl", table: "Staff",
                type: "text", nullable: true);
            mb.AddColumn<string>(
                name: "MagicToken", table: "Staff",
                type: "text", nullable: true);
            mb.AddColumn<DateTime>(
                name: "MagicTokenExpiry", table: "Staff",
                type: "timestamp with time zone", nullable: true);

            // Restore QrCodes table
            mb.CreateTable(
                name: "QrCodes",
                columns: table => new
                {
                    Id         = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token      = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAt  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked  = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QrCodes", x => x.Id);
                    table.ForeignKey("FK_QrCodes_Staff_EmployeeId",
                        x => x.EmployeeId, "Staff", "Id", onDelete: ReferentialAction.Cascade);
                });

            // Restore ShiftSwapRequests table
            mb.CreateTable(
                name: "ShiftSwapRequests",
                columns: table => new
                {
                    Id               = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId      = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequesterShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetShiftId    = table.Column<Guid>(type: "uuid", nullable: true),
                    Status           = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason           = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedById     = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewNotes      = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_ShiftSwapRequests", x => x.Id));

            // Restore RosterEntries table
            mb.CreateTable(
                name: "RosterEntries",
                columns: table => new
                {
                    Id                = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId        = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId        = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocationName      = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShiftDate         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartTime         = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    EndTime           = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Role              = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes             = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsConfirmedByStaff = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ConfirmedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VenueEventId      = table.Column<Guid>(type: "uuid", nullable: true),
                    VenueEventName    = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterEntries", x => x.Id);
                    table.ForeignKey("FK_RosterEntries_Staff_EmployeeId",
                        x => x.EmployeeId, "Staff", "Id", onDelete: ReferentialAction.Cascade);
                });

            // Restore IncidentRecords
            mb.CreateTable(
                name: "IncidentRecords",
                columns: table => new
                {
                    Id           = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId   = table.Column<Guid>(type: "uuid", nullable: false),
                    Type         = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description  = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IssuedById   = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentRecords", x => x.Id);
                    table.ForeignKey("FK_IncidentRecords_Staff_EmployeeId",
                        x => x.EmployeeId, "Staff", "Id", onDelete: ReferentialAction.Cascade);
                });

            // Restore VenueEvents
            mb.CreateTable(
                name: "VenueEvents",
                columns: table => new
                {
                    Id             = table.Column<Guid>(type: "uuid", nullable: false),
                    Name           = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date           = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LocationId     = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LocationName   = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description    = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExpectedGuests = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_VenueEvents", x => x.Id));
        }
    }
}
