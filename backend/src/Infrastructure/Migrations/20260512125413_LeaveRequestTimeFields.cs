using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaveRequestTimeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop tables IF EXISTS (they may already be absent in newer deployments)
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ShiftSwapRequests"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""RosterEntries"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ClockLogs"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""QrCodes"";");

            // Drop columns IF EXISTS on Staff
            migrationBuilder.Sql(@"ALTER TABLE ""Staff"" DROP COLUMN IF EXISTS ""HourlyRate"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Staff"" DROP COLUMN IF EXISTS ""IcDocumentUrl"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Staff"" DROP COLUMN IF EXISTS ""MagicToken"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Staff"" DROP COLUMN IF EXISTS ""MagicTokenExpiry"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Staff"" DROP COLUMN IF EXISTS ""PassportDocumentUrl"";");

            // Drop Period column IF EXISTS on LeaveRequests
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" DROP COLUMN IF EXISTS ""Period"";" );

            // Add columns IF NOT EXISTS (idempotent)
            migrationBuilder.Sql(@"ALTER TABLE ""Staff"" ADD COLUMN IF NOT EXISTS ""PreferredLanguage"" text NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""Roles"" ADD COLUMN IF NOT EXISTS ""HourlyRate"" numeric(10,2) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN IF NOT EXISTS ""EndTime"" text;");
            migrationBuilder.Sql(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN IF NOT EXISTS ""StartTime"" text;");

            // Create tables IF NOT EXISTS (idempotent)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""FeatureFlags"" (
                    ""Key"" character varying(100) NOT NULL,
                    ""IsEnabled"" boolean NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_FeatureFlags"" PRIMARY KEY (""Key"")
                );");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""StaffAvailabilities"" (
                    ""Id"" uuid NOT NULL,
                    ""EmployeeId"" uuid NOT NULL,
                    ""DayOfWeek"" integer NOT NULL,
                    ""IsUnavailable"" boolean NOT NULL,
                    ""StartTime"" character varying(5),
                    ""EndTime"" character varying(5),
                    ""Note"" character varying(500),
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_StaffAvailabilities"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_StaffAvailabilities_Staff_EmployeeId"" FOREIGN KEY (""EmployeeId"")
                        REFERENCES ""Staff"" (""Id"") ON DELETE CASCADE
                );");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""StaffMessages"" (
                    ""Id"" uuid NOT NULL,
                    ""SenderId"" uuid NOT NULL,
                    ""SenderName"" character varying(200) NOT NULL,
                    ""SenderIsAdmin"" boolean NOT NULL,
                    ""RecipientId"" uuid,
                    ""RecipientName"" character varying(200),
                    ""Content"" character varying(2000) NOT NULL,
                    ""SentAt"" timestamp with time zone NOT NULL,
                    ""ReadAt"" timestamp with time zone,
                    CONSTRAINT ""PK_StaffMessages"" PRIMARY KEY (""Id"")
                );");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""WorkLogs"" (
                    ""Id"" uuid NOT NULL,
                    ""EmployeeId"" uuid NOT NULL,
                    ""RoleName"" character varying(100) NOT NULL,
                    ""LocationId"" character varying(100) NOT NULL,
                    ""LocationName"" character varying(200) NOT NULL,
                    ""WorkDate"" date NOT NULL,
                    ""StartTime"" character varying(5) NOT NULL,
                    ""EndTime"" character varying(5),
                    ""IsManualEntry"" boolean NOT NULL,
                    ""Notes"" character varying(500),
                    ""HourlyRateSnapshot"" numeric(10,2) NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_WorkLogs"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_WorkLogs_Staff_EmployeeId"" FOREIGN KEY (""EmployeeId"")
                        REFERENCES ""Staff"" (""Id"") ON DELETE CASCADE
                );");

            // Create indexes IF NOT EXISTS
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_StaffAvailabilities_EmployeeId"" ON ""StaffAvailabilities"" (""EmployeeId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_StaffMessages_RecipientId"" ON ""StaffMessages"" (""RecipientId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_StaffMessages_SenderId"" ON ""StaffMessages"" (""SenderId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_StaffMessages_SentAt"" ON ""StaffMessages"" (""SentAt"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_WorkLogs_EmployeeId"" ON ""WorkLogs"" (""EmployeeId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_WorkLogs_WorkDate"" ON ""WorkLogs"" (""WorkDate"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureFlags");

            migrationBuilder.DropTable(
                name: "StaffAvailabilities");

            migrationBuilder.DropTable(
                name: "StaffMessages");

            migrationBuilder.DropTable(
                name: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "HourlyRate",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "LeaveRequests");

            migrationBuilder.AddColumn<decimal>(
                name: "HourlyRate",
                table: "Staff",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "IcDocumentUrl",
                table: "Staff",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MagicToken",
                table: "Staff",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MagicTokenExpiry",
                table: "Staff",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportDocumentUrl",
                table: "Staff",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Period",
                table: "LeaveRequests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ClockLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    HourlyRateSnapshot = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    IsManuallyAdjusted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LocationId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    QrToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TipsAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClockLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClockLogs_Staff_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QrCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    LocationId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Token = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QrCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RosterEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    LocationId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Staff_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftSwapRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RosterEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequesterNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetRosterEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftSwapRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftSwapRequests_RosterEntries_RosterEntryId",
                        column: x => x.RosterEntryId,
                        principalTable: "RosterEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftSwapRequests_Staff_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClockLogs_EmployeeId",
                table: "ClockLogs",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClockLogs_Timestamp",
                table: "ClockLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_QrCodes_Token",
                table: "QrCodes",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_Date",
                table: "RosterEntries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_EmployeeId",
                table: "RosterEntries",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSwapRequests_CreatedAt",
                table: "ShiftSwapRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSwapRequests_RequesterId",
                table: "ShiftSwapRequests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSwapRequests_RosterEntryId",
                table: "ShiftSwapRequests",
                column: "RosterEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSwapRequests_Status",
                table: "ShiftSwapRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSwapRequests_TargetEmployeeId",
                table: "ShiftSwapRequests",
                column: "TargetEmployeeId");
        }
    }
}
