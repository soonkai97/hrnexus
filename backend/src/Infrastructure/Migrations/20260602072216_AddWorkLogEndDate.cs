using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkLogEndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "WorkLogs",
                type: "date",
                nullable: true);

            // Backfill existing CLOSED shifts: the end date is the work date, or the next day when the
            // end time is at or before the start time (the historical single-overnight assumption).
            // Time strings are "HH:mm" so a lexicographic comparison is correct. Active shifts
            // (EndTime IS NULL) keep EndDate NULL.
            migrationBuilder.Sql(@"
                UPDATE ""WorkLogs""
                SET ""EndDate"" = CASE
                    WHEN ""EndTime"" <= ""StartTime"" THEN ""WorkDate"" + 1
                    ELSE ""WorkDate""
                END
                WHERE ""EndTime"" IS NOT NULL AND ""EndDate"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "WorkLogs");
        }
    }
}
