using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 7o: access is decided by permissions, never by a job's name, so the names SUPERADMIN and MANAGER
    /// retire. No table changes; this only moves data, and only when those rows exist.
    /// </summary>
    public partial class RetireRoleNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    sa  uuid;
                    adm uuid;
                BEGIN
                    SELECT ""Id"" INTO sa  FROM ""Roles"" WHERE ""Name"" = 'SUPERADMIN';
                    SELECT ""Id"" INTO adm FROM ""Roles"" WHERE ""Name"" = 'ADMINISTRATOR';

                    IF sa IS NOT NULL THEN
                        IF adm IS NULL THEN
                            -- The first administrator job keeps its members and permissions under its new name.
                            UPDATE ""Roles"" SET ""Name"" = 'ADMINISTRATOR', ""IsSystem"" = true WHERE ""Id"" = sa;
                        ELSE
                            -- A job called ADMINISTRATOR already exists (the name is unique): fold SUPERADMIN into
                            -- it, so nobody loses access, and drop the old row.
                            UPDATE ""Roles"" SET ""IsSystem"" = true WHERE ""Id"" = adm;
                            INSERT INTO ""StaffRoles"" (""EmployeeId"", ""RoleId"")
                                SELECT ""EmployeeId"", adm FROM ""StaffRoles"" WHERE ""RoleId"" = sa
                                ON CONFLICT DO NOTHING;
                            INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionKey"")
                                SELECT adm, ""PermissionKey"" FROM ""RolePermissions"" WHERE ""RoleId"" = sa
                                ON CONFLICT DO NOTHING;
                            DELETE FROM ""StaffRoles""      WHERE ""RoleId"" = sa;
                            DELETE FROM ""RolePermissions"" WHERE ""RoleId"" = sa;
                            DELETE FROM ""Roles""           WHERE ""Id""     = sa;
                        END IF;
                    END IF;

                    -- MANAGER becomes an ordinary job: it keeps its members and permissions, but is no longer a
                    -- system job (it can be edited or deleted like any other, and no longer receives every new
                    -- permission automatically).
                    UPDATE ""Roles"" SET ""IsSystem"" = false WHERE ""Name"" = 'MANAGER';
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Run this BEFORE reverting the code: the old seed would otherwise create a second SUPERADMIN.
            // The first administrator job is found by its flag, so a renamed job is still found. A fold
            // (an ADMINISTRATOR job already existed) is not undone.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM ""Roles"" WHERE ""Name"" = 'SUPERADMIN') THEN
                        UPDATE ""Roles"" SET ""Name"" = 'SUPERADMIN'
                        WHERE ""Id"" = (SELECT ""Id"" FROM ""Roles"" WHERE ""IsSystem"" = true
                                        ORDER BY ""CreatedAt"", ""Name"" LIMIT 1);
                    END IF;
                    UPDATE ""Roles"" SET ""IsSystem"" = true WHERE ""Name"" = 'MANAGER';
                END $$;
            ");
        }
    }
}
