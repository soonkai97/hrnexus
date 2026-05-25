using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HRNexus.Infrastructure.Persistence;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260511200000_AddPreferredLanguage")]
    public partial class AddPreferredLanguage : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            mb.AddColumn<string>(
                name:         "PreferredLanguage",
                table:        "Staff",
                type:         "character varying(10)",
                maxLength:    10,
                nullable:     false,
                defaultValue: "en");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.DropColumn(name: "PreferredLanguage", table: "Staff");
        }
    }
}
