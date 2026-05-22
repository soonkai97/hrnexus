using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename UpdatedAt → ModifiedAt to match IHasAudit convention; preserve existing data.
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "StaffExpenses",
                newName: "ModifiedAt");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffExpenses",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: false);

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "StaffEntries",
                newName: "ModifiedAt");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffEntries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: false);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "WorkLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "WorkLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "WorkLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WorkLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "WorkLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "WorkLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "StaffWorkTypes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "StaffWorkTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffWorkTypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "StaffWorkTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "StaffSettlements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "StaffSettlements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffSettlements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "StaffSettlements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "StaffLoanTypes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "StaffLoanTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffLoanTypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "StaffLoanTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "StaffLoans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "StaffLoans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "StaffLoans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffLoans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "StaffLoans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "StaffExpenses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "StaffExpenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "StaffExpenses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "StaffExpenses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "StaffExpenseCategories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "StaffExpenseCategories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffExpenseCategories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "StaffExpenseCategories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "StaffEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "StaffEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "StaffEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "StaffEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Staff",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Staff",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Staff",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Staff",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "Staff",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Staff",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "WorkLogs");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "StaffWorkTypes");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StaffWorkTypes");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "StaffWorkTypes");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "StaffWorkTypes");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "StaffSettlements");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StaffSettlements");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "StaffSettlements");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "StaffSettlements");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "StaffLoanTypes");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StaffLoanTypes");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "StaffLoanTypes");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "StaffLoanTypes");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StaffLoans");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "StaffLoans");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "StaffLoans");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "StaffLoans");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "StaffLoans");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StaffExpenses");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "StaffExpenses");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "StaffExpenses");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "StaffExpenses");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "StaffExpenseCategories");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StaffExpenseCategories");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "StaffExpenseCategories");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "StaffExpenseCategories");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StaffEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "StaffEntries");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "StaffEntries");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "StaffEntries");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Staff");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffExpenses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "StaffExpenses",
                newName: "UpdatedAt");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAt",
                table: "StaffEntries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "StaffEntries",
                newName: "UpdatedAt");
        }
    }
}
