using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NightclubWorkforce.Infrastructure.Persistence;

#nullable disable

namespace NightclubWorkforce.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260522000000_UppercaseRoleNames")]
    public partial class UppercaseRoleNames : Migration
    {
        /// <summary>
        /// Converts all role names to UPPER CASE across every table that stores them.
        ///
        ///   Roles.Name         — primary key; used as the authoritative role registry
        ///   StaffRoles.Role    — per-employee role assignments
        ///   WorkLogs.RoleName  — denormalised snapshot stored at shift-log time
        ///
        /// NOTE: Roles.Name is the PK of the Roles table.  PostgreSQL allows UPDATE
        /// on a PK column in a single statement because no FK in this schema references
        /// Roles.Name (StaffRoles.Role is stored independently, not as a formal FK).
        /// Both tables are updated atomically within the same migration transaction.
        /// </summary>
        protected override void Up(MigrationBuilder mb)
        {
            // 1. Roles registry — primary key column
            mb.Sql(@"UPDATE ""Roles"" SET ""Name"" = UPPER(""Name"")");

            // 2. Staff role assignments
            mb.Sql(@"UPDATE ""StaffRoles"" SET ""Role"" = UPPER(""Role"")");

            // 3. WorkLog role-name snapshots
            mb.Sql(@"UPDATE ""WorkLogs"" SET ""RoleName"" = UPPER(""RoleName"")");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql(@"UPDATE ""Roles"" SET ""Name"" = LOWER(""Name"")");
            mb.Sql(@"UPDATE ""StaffRoles"" SET ""Role"" = LOWER(""Role"")");
            mb.Sql(@"UPDATE ""WorkLogs"" SET ""RoleName"" = LOWER(""RoleName"")");
        }
    }
}
