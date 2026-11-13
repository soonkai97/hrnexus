using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The permission key no longer exists in the catalog; remove any grants so the
            // role and per-user permission screens never see an unknown key.
            migrationBuilder.Sql(@"
                DELETE FROM ""RolePermissions"" WHERE ""PermissionKey"" = 'locations.manage';
                DELETE FROM ""StaffPermissionOverrides"" WHERE ""PermissionKey"" = 'locations.manage';
            ");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "StaffLocations");
        }

        /// <inheritdoc />
        /// <remarks>Recreates the empty tables only: location rows, staff assignments and
        /// <c>locations.manage</c> grants are not restored.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ImagePath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameVi = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NameZh = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffLocations",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLocations", x => new { x.EmployeeId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_StaffLocations_Staff_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name_Active",
                table: "Locations",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
