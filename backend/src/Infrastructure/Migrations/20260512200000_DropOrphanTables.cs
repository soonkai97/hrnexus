using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    /// <summary>
    /// Drops ShiftTemplateEntries and ShiftTemplates which were re-created by
    /// AddNewFeatures (20260511160000) after being dropped by DropShiftTemplates
    /// (20260511105628). WorkLogAndRoleRate incorrectly assumed they were already gone.
    /// </summary>
    public partial class DropOrphanTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ShiftTemplateEntries must be dropped before ShiftTemplates (FK)
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ShiftTemplateEntries"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ShiftTemplates"";");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""ShiftTemplates"" (
                    ""Id"" uuid NOT NULL,
                    ""Name"" character varying(200) NOT NULL,
                    ""Description"" character varying(500),
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_ShiftTemplates"" PRIMARY KEY (""Id"")
                );");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""ShiftTemplateEntries"" (
                    ""Id"" uuid NOT NULL,
                    ""TemplateId"" uuid NOT NULL,
                    ""DayOfWeek"" integer NOT NULL,
                    ""LocationId"" character varying(50) NOT NULL,
                    ""LocationName"" character varying(100) NOT NULL,
                    ""StartTime"" character varying(5) NOT NULL,
                    ""EndTime"" character varying(5) NOT NULL,
                    ""RoleHint"" character varying(100),
                    ""Notes"" character varying(500),
                    CONSTRAINT ""PK_ShiftTemplateEntries"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_ShiftTemplateEntries_ShiftTemplates_TemplateId""
                        FOREIGN KEY (""TemplateId"") REFERENCES ""ShiftTemplates"" (""Id"") ON DELETE CASCADE
                );");
        }
    }
}
