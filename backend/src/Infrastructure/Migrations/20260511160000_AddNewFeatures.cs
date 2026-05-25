using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRNexus.Infrastructure.Persistence;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260511160000_AddNewFeatures")]
    public partial class AddNewFeatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── StaffAvailabilities ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "StaffAvailabilities",
                columns: table => new
                {
                    Id           = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId   = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek    = table.Column<int>(type: "integer", nullable: false),
                    IsUnavailable = table.Column<bool>(type: "boolean", nullable: false),
                    StartTime    = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    EndTime      = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Note         = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAvailabilities", x => x.Id);
                    table.ForeignKey("FK_StaffAvailabilities_Staff_EmployeeId",
                        x => x.EmployeeId, "Staff", "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex("IX_StaffAvailabilities_EmployeeId", "StaffAvailabilities", "EmployeeId");

            // ── ShiftTemplates ──────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ShiftTemplates",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "uuid", nullable: false),
                    Name        = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_ShiftTemplates", x => x.Id));

            migrationBuilder.CreateTable(
                name: "ShiftTemplateEntries",
                columns: table => new
                {
                    Id           = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId   = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek    = table.Column<int>(type: "integer", nullable: false),
                    LocationId   = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime    = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    EndTime      = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    RoleHint     = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes        = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTemplateEntries", x => x.Id);
                    table.ForeignKey("FK_ShiftTemplateEntries_ShiftTemplates_TemplateId",
                        x => x.TemplateId, "ShiftTemplates", "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex("IX_ShiftTemplateEntries_TemplateId", "ShiftTemplateEntries", "TemplateId");

            // ── StaffMessages ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "StaffMessages",
                columns: table => new
                {
                    Id            = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId      = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderName    = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenderIsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    RecipientId   = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content       = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SentAt        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt        = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_StaffMessages", x => x.Id));
            migrationBuilder.CreateIndex("IX_StaffMessages_SenderId", "StaffMessages", "SenderId");
            migrationBuilder.CreateIndex("IX_StaffMessages_RecipientId", "StaffMessages", "RecipientId");
            migrationBuilder.CreateIndex("IX_StaffMessages_SentAt", "StaffMessages", "SentAt");

            // ── IncidentRecords ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
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
            migrationBuilder.CreateIndex("IX_IncidentRecords_EmployeeId", "IncidentRecords", "EmployeeId");
            migrationBuilder.CreateIndex("IX_IncidentRecords_IssuedAt", "IncidentRecords", "IssuedAt");

            // ── VenueEvents ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
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
            migrationBuilder.CreateIndex("IX_VenueEvents_Date", "VenueEvents", "Date");

            // ── RosterEntries – add new columns ─────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "VenueEventId", table: "RosterEntries",
                type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "VenueEventName", table: "RosterEntries",
                type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "IsConfirmedByStaff", table: "RosterEntries",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt", table: "RosterEntries",
                type: "timestamp with time zone", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("ShiftTemplateEntries");
            migrationBuilder.DropTable("ShiftTemplates");
            migrationBuilder.DropTable("StaffAvailabilities");
            migrationBuilder.DropTable("StaffMessages");
            migrationBuilder.DropTable("IncidentRecords");
            migrationBuilder.DropTable("VenueEvents");
            migrationBuilder.DropColumn("VenueEventId", "RosterEntries");
            migrationBuilder.DropColumn("VenueEventName", "RosterEntries");
            migrationBuilder.DropColumn("IsConfirmedByStaff", "RosterEntries");
            migrationBuilder.DropColumn("ConfirmedAt", "RosterEntries");
        }
    }
}
