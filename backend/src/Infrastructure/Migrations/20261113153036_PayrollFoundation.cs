using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleRates");

            migrationBuilder.DropColumn(
                name: "HourlyRate",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PayType",
                table: "Roles");

            migrationBuilder.AddColumn<string>(
                name: "CitizenshipStatus",
                table: "Staff",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentEndDate",
                table: "Staff",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentStartDate",
                table: "Staff",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalaryProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    BasicSalary = table.Column<string>(type: "text", nullable: false),
                    PayFrequency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankAccount = table.Column<string>(type: "text", nullable: true),
                    BankHolder = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryProfiles_Staff_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryProfiles_EmployeeId_EffectiveFrom",
                table: "SalaryProfiles",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalaryProfiles");

            migrationBuilder.DropColumn(
                name: "CitizenshipStatus",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "EmploymentEndDate",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "EmploymentStartDate",
                table: "Staff");

            migrationBuilder.AddColumn<decimal>(
                name: "HourlyRate",
                table: "Roles",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PayType",
                table: "Roles",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RoleRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleRates_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleRates_RoleId_EffectiveOn",
                table: "RoleRates",
                columns: new[] { "RoleId", "EffectiveOn" });
        }
    }
}
