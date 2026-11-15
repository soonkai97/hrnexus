using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReportsToId",
                table: "Staff",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RepaymentPerMonth",
                table: "CashAdvances",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<string>(type: "text", nullable: false),
                    CountsForEpf = table.Column<bool>(type: "boolean", nullable: false),
                    CountsForSocsoEis = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayItems_Staff_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Cycle = table.Column<int>(type: "integer", nullable: false),
                    CalculatorVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Divisor = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    HoursPerWorkingDay = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    DeductionCapRate = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    ExportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashAdvanceRepayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CashAdvanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashAdvanceRepayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashAdvanceRepayments_CashAdvances_CashAdvanceId",
                        column: x => x.CashAdvanceId,
                        principalTable: "CashAdvances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashAdvanceRepayments_PayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunActors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Cycle = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunActors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunActors_PayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Excluded = table.Column<bool>(type: "boolean", nullable: false),
                    ExcludedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PcbAmount = table.Column<string>(type: "text", nullable: true),
                    Calculated = table.Column<bool>(type: "boolean", nullable: false),
                    Gross = table.Column<string>(type: "text", nullable: false),
                    Net = table.Column<string>(type: "text", nullable: false),
                    EmployerCost = table.Column<string>(type: "text", nullable: false),
                    UnappliedDeductions = table.Column<string>(type: "text", nullable: false),
                    SignedOffBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SignedOffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunLines_PayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollRunLines_Staff_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Section = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<string>(type: "text", nullable: false),
                    RateRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunLineItems_PayrollRunLines_LineId",
                        column: x => x.LineId,
                        principalTable: "PayrollRunLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunUnpaidDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    LeaveId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeductionAmount = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunUnpaidDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunUnpaidDays_PayrollRunLines_LineId",
                        column: x => x.LineId,
                        principalTable: "PayrollRunLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunWarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Blocking = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunWarnings_PayrollRunLines_LineId",
                        column: x => x.LineId,
                        principalTable: "PayrollRunLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Staff_ReportsToId",
                table: "Staff",
                column: "ReportsToId");

            migrationBuilder.CreateIndex(
                name: "IX_CashAdvanceRepayments_CashAdvanceId_RunId",
                table: "CashAdvanceRepayments",
                columns: new[] { "CashAdvanceId", "RunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashAdvanceRepayments_RunId",
                table: "CashAdvanceRepayments",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayItems_EmployeeId",
                table: "PayItems",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunActors_RunId_StaffId",
                table: "PayrollRunActors",
                columns: new[] { "RunId", "StaffId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunLineItems_LineId",
                table: "PayrollRunLineItems",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunLines_EmployeeId",
                table: "PayrollRunLines",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunLines_RunId_EmployeeId",
                table: "PayrollRunLines",
                columns: new[] { "RunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_Year_Month",
                table: "PayrollRuns",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunUnpaidDays_LineId",
                table: "PayrollRunUnpaidDays",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunWarnings_LineId",
                table: "PayrollRunWarnings",
                column: "LineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Staff_Staff_ReportsToId",
                table: "Staff",
                column: "ReportsToId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Staff_Staff_ReportsToId",
                table: "Staff");

            migrationBuilder.DropTable(
                name: "CashAdvanceRepayments");

            migrationBuilder.DropTable(
                name: "PayItems");

            migrationBuilder.DropTable(
                name: "PayrollRunActors");

            migrationBuilder.DropTable(
                name: "PayrollRunLineItems");

            migrationBuilder.DropTable(
                name: "PayrollRunUnpaidDays");

            migrationBuilder.DropTable(
                name: "PayrollRunWarnings");

            migrationBuilder.DropTable(
                name: "PayrollRunLines");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_Staff_ReportsToId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "ReportsToId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "RepaymentPerMonth",
                table: "CashAdvances");
        }
    }
}
