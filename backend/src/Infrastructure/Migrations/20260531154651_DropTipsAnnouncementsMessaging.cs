using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropTipsAnnouncementsMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clean up references to the removed features: permission grants + feature-flag rows.
            migrationBuilder.Sql(@"DELETE FROM ""RolePermissions"" WHERE ""PermissionKey"" IN ('tips.manage', 'announcements.manage');");
            migrationBuilder.Sql(@"DELETE FROM ""StaffPermissionOverrides"" WHERE ""PermissionKey"" IN ('tips.manage', 'announcements.manage');");
            migrationBuilder.Sql(@"DELETE FROM ""FeatureFlags"" WHERE ""Key"" IN ('tips', 'tipsHistory', 'announcements');");

            migrationBuilder.DropTable(
                name: "AnnouncementReads");

            migrationBuilder.DropTable(
                name: "StaffMessages");

            migrationBuilder.DropTable(
                name: "TipsAllocations");

            migrationBuilder.DropTable(
                name: "TipsPools");

            migrationBuilder.DropTable(
                name: "Announcements");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TargetRoles = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderIsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    SenderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TipsAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HoursWorked = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    PoolId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipsAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TipsPools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDistributed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShiftDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SplitMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StaffCount = table.Column<int>(type: "integer", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipsPools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementReads",
                columns: table => new
                {
                    AnnouncementId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementReads", x => new { x.AnnouncementId, x.EmployeeId });
                    table.ForeignKey(
                        name: "FK_AnnouncementReads_Announcements_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "Announcements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_CreatedAt",
                table: "Announcements",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_IsActive",
                table: "Announcements",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMessages_RecipientId",
                table: "StaffMessages",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMessages_SenderId",
                table: "StaffMessages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMessages_SentAt",
                table: "StaffMessages",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_TipsAllocations_PoolId_EmployeeId",
                table: "TipsAllocations",
                columns: new[] { "PoolId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_TipsPools_LocationId",
                table: "TipsPools",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TipsPools_LocationId_ShiftDate",
                table: "TipsPools",
                columns: new[] { "LocationId", "ShiftDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TipsPools_ShiftDate",
                table: "TipsPools",
                column: "ShiftDate");
        }
    }
}
