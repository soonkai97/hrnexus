using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompanyPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    WorkingDaysMask = table.Column<int>(type: "integer", nullable: false),
                    WorkdayHours = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    AnnualLeaveAdvanceDays = table.Column<int>(type: "integer", nullable: false),
                    MaxLeaveDays = table.Column<int>(type: "integer", nullable: false),
                    CarryOverMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CarryOverMaxHours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    UnpaidLeaveDivisor = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyPolicies", x => x.Id);
                    table.CheckConstraint("CK_CompanyPolicy_SingleRow", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyPolicies");
        }
    }
}
