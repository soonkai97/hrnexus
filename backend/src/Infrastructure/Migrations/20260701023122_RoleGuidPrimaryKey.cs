using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRNexus.Infrastructure.Migrations
{
    /// <summary>
    /// Switches the Role primary key from the free-text Name to a GUID Id, and re-points the three
    /// name-keyed child tables (StaffRoles, RolePermissions, RoleRates) at that Id. Name becomes a
    /// normal (still-unique) display column. WorkLogs keep their RoleName snapshot untouched — that
    /// column is a frozen historical value, not a foreign key.
    ///
    /// The Up() is HAND-WRITTEN (not the scaffold) so it PRESERVES data: it generates a stable GUID
    /// per existing role, then backfills every child row's RoleId by joining old name → new Id BEFORE
    /// dropping the old name columns. Column-add/backfill/constraint ordering is chosen to satisfy
    /// PostgreSQL (a PK can't be dropped while an FK references it; a NOT NULL column must be
    /// backfilled before the constraint is added).
    ///
    /// ⚠️ Deploy in a maintenance window with a fresh DB backup.
    /// </summary>
    public partial class RoleGuidPrimaryKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // gen_random_uuid() lives in pgcrypto (bundled with Postgres); ensure it's available.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";");

            // ── 1. Roles: add Id, backfill a stable GUID per existing role ───────────────────────
            migrationBuilder.Sql("ALTER TABLE \"Roles\" ADD COLUMN \"Id\" uuid;");
            migrationBuilder.Sql("UPDATE \"Roles\" SET \"Id\" = gen_random_uuid() WHERE \"Id\" IS NULL;");

            // ── 2. Add nullable RoleId to the three child tables; backfill by joining on name ─────
            migrationBuilder.Sql("ALTER TABLE \"StaffRoles\" ADD COLUMN \"RoleId\" uuid;");
            migrationBuilder.Sql(
                "UPDATE \"StaffRoles\" sr SET \"RoleId\" = r.\"Id\" FROM \"Roles\" r WHERE sr.\"Role\" = r.\"Name\";");

            migrationBuilder.Sql("ALTER TABLE \"RolePermissions\" ADD COLUMN \"RoleId\" uuid;");
            migrationBuilder.Sql(
                "UPDATE \"RolePermissions\" rp SET \"RoleId\" = r.\"Id\" FROM \"Roles\" r WHERE rp.\"RoleName\" = r.\"Name\";");

            migrationBuilder.Sql("ALTER TABLE \"RoleRates\" ADD COLUMN \"RoleId\" uuid;");
            migrationBuilder.Sql(
                "UPDATE \"RoleRates\" rr SET \"RoleId\" = r.\"Id\" FROM \"Roles\" r WHERE rr.\"RoleName\" = r.\"Name\";");

            // Any child rows that failed to match a role (orphans) would break the NOT NULL / FK steps.
            // Delete orphaned permission/rate rows (safe: they reference a non-existent role); a
            // StaffRoles orphan is likewise meaningless without a role.
            migrationBuilder.Sql("DELETE FROM \"StaffRoles\"      WHERE \"RoleId\" IS NULL;");
            migrationBuilder.Sql("DELETE FROM \"RolePermissions\" WHERE \"RoleId\" IS NULL;");
            migrationBuilder.Sql("DELETE FROM \"RoleRates\"       WHERE \"RoleId\" IS NULL;");

            // ── 3. Drop the old name-based keys/index (must precede dropping the referenced PK) ───
            migrationBuilder.DropPrimaryKey(name: "PK_StaffRoles",      table: "StaffRoles");
            migrationBuilder.DropPrimaryKey(name: "PK_RolePermissions", table: "RolePermissions");
            migrationBuilder.DropIndex(name: "IX_RoleRates_RoleName_EffectiveOn", table: "RoleRates");
            migrationBuilder.DropPrimaryKey(name: "PK_Roles", table: "Roles");

            // ── 4. Drop the now-redundant old name columns on the child tables ───────────────────
            migrationBuilder.DropColumn(name: "Role",     table: "StaffRoles");
            migrationBuilder.DropColumn(name: "RoleName", table: "RolePermissions");
            migrationBuilder.DropColumn(name: "RoleName", table: "RoleRates");

            // ── 5. Enforce NOT NULL now that every value is backfilled ───────────────────────────
            migrationBuilder.Sql("ALTER TABLE \"Roles\"           ALTER COLUMN \"Id\"     SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE \"StaffRoles\"      ALTER COLUMN \"RoleId\" SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE \"RolePermissions\" ALTER COLUMN \"RoleId\" SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE \"RoleRates\"       ALTER COLUMN \"RoleId\" SET NOT NULL;");

            // ── 6. Re-create keys, unique Name index, effective-dated index and FKs on RoleId ────
            migrationBuilder.AddPrimaryKey(name: "PK_Roles", table: "Roles", column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name", table: "Roles", column: "Name", unique: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_StaffRoles", table: "StaffRoles", columns: new[] { "EmployeeId", "RoleId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_RolePermissions", table: "RolePermissions", columns: new[] { "RoleId", "PermissionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoles_RoleId", table: "StaffRoles", column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleRates_RoleId_EffectiveOn", table: "RoleRates", columns: new[] { "RoleId", "EffectiveOn" });

            migrationBuilder.AddForeignKey(
                name: "FK_StaffRoles_Roles_RoleId", table: "StaffRoles", column: "RoleId",
                principalTable: "Roles", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Roles_RoleId", table: "RolePermissions", column: "RoleId",
                principalTable: "Roles", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleRates_Roles_RoleId", table: "RoleRates", column: "RoleId",
                principalTable: "Roles", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: re-add the name columns, backfill them by joining on Role.Id, then restore the
            // name-based keys. Data is preserved on the way back down too.
            migrationBuilder.DropForeignKey(name: "FK_StaffRoles_Roles_RoleId",      table: "StaffRoles");
            migrationBuilder.DropForeignKey(name: "FK_RolePermissions_Roles_RoleId", table: "RolePermissions");
            migrationBuilder.DropForeignKey(name: "FK_RoleRates_Roles_RoleId",       table: "RoleRates");

            migrationBuilder.DropPrimaryKey(name: "PK_StaffRoles",      table: "StaffRoles");
            migrationBuilder.DropPrimaryKey(name: "PK_RolePermissions", table: "RolePermissions");
            migrationBuilder.DropIndex(name: "IX_StaffRoles_RoleId",              table: "StaffRoles");
            migrationBuilder.DropIndex(name: "IX_RoleRates_RoleId_EffectiveOn",   table: "RoleRates");
            migrationBuilder.DropIndex(name: "IX_Roles_Name",                     table: "Roles");
            migrationBuilder.DropPrimaryKey(name: "PK_Roles", table: "Roles");

            // Re-add nullable name columns and backfill from Role.Id before dropping the Id columns.
            migrationBuilder.Sql("ALTER TABLE \"StaffRoles\"      ADD COLUMN \"Role\"     character varying(50);");
            migrationBuilder.Sql(
                "UPDATE \"StaffRoles\" sr SET \"Role\" = r.\"Name\" FROM \"Roles\" r WHERE sr.\"RoleId\" = r.\"Id\";");

            migrationBuilder.Sql("ALTER TABLE \"RolePermissions\" ADD COLUMN \"RoleName\" character varying(50);");
            migrationBuilder.Sql(
                "UPDATE \"RolePermissions\" rp SET \"RoleName\" = r.\"Name\" FROM \"Roles\" r WHERE rp.\"RoleId\" = r.\"Id\";");

            migrationBuilder.Sql("ALTER TABLE \"RoleRates\"       ADD COLUMN \"RoleName\" character varying(50);");
            migrationBuilder.Sql(
                "UPDATE \"RoleRates\" rr SET \"RoleName\" = r.\"Name\" FROM \"Roles\" r WHERE rr.\"RoleId\" = r.\"Id\";");

            migrationBuilder.Sql("ALTER TABLE \"StaffRoles\"      ALTER COLUMN \"Role\"     SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE \"RolePermissions\" ALTER COLUMN \"RoleName\" SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE \"RoleRates\"       ALTER COLUMN \"RoleName\" SET NOT NULL;");

            migrationBuilder.DropColumn(name: "RoleId", table: "StaffRoles");
            migrationBuilder.DropColumn(name: "RoleId", table: "RolePermissions");
            migrationBuilder.DropColumn(name: "RoleId", table: "RoleRates");
            migrationBuilder.DropColumn(name: "Id",     table: "Roles");

            migrationBuilder.AddPrimaryKey(name: "PK_Roles", table: "Roles", column: "Name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StaffRoles", table: "StaffRoles", columns: new[] { "EmployeeId", "Role" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_RolePermissions", table: "RolePermissions", columns: new[] { "RoleName", "PermissionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleRates_RoleName_EffectiveOn", table: "RoleRates", columns: new[] { "RoleName", "EffectiveOn" });
        }
    }
}
