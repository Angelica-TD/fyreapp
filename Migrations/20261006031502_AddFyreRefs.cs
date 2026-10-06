using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddFyreRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "Sites",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "SiteContacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "ServiceReports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "MaintenanceSchedules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "Defects",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "ClientTasks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "Clients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FyreRef",
                table: "Assets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_FyreRef",
                table: "Sites",
                column: "FyreRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteContacts_FyreRef",
                table: "SiteContacts",
                column: "FyreRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReports_FyreRef",
                table: "ServiceReports",
                column: "FyreRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSchedules_FyreRef",
                table: "MaintenanceSchedules",
                column: "FyreRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Defects_FyreRef",
                table: "Defects",
                column: "FyreRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_FyreRef",
                table: "ClientTasks",
                column: "FyreRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_FyreRef",
                table: "Clients",
                column: "FyreRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_FyreRef",
                table: "Assets",
                column: "FyreRef",
                unique: true);

            // Assigns "<prefix>-<next number>" on insert when the row has no Uptick ID.
            // ExternalId is read via jsonb so tables without that column (tasks, schedules) work too.
            migrationBuilder.Sql(@"
CREATE FUNCTION fyre_assign_ref() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.""FyreRef"" IS NULL AND coalesce(to_jsonb(NEW)->>'ExternalId', '') = '' THEN
        NEW.""FyreRef"" := TG_ARGV[0] || '-' || nextval(TG_ARGV[1]::regclass);
    END IF;
    RETURN NEW;
END;
$$;");

            foreach (var (table, prefix) in RefTables)
            {
                var seq = $"{table}_FyreRef_seq";
                migrationBuilder.Sql($@"CREATE SEQUENCE ""{seq}"" START 1001;");

                // Existing records with no Uptick ID get a ref now, oldest first
                migrationBuilder.Sql($@"
UPDATE ""{table}"" t
SET ""FyreRef"" = '{prefix}-' || nextval('""{seq}""')
FROM (
    SELECT ""Id"" FROM ""{table}"" r
    WHERE coalesce(to_jsonb(r)->>'ExternalId', '') = ''
    ORDER BY ""Id""
) o
WHERE t.""Id"" = o.""Id"";");

                migrationBuilder.Sql($@"
CREATE TRIGGER ""{table}_assign_fyre_ref""
BEFORE INSERT ON ""{table}""
FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('{prefix}', '""{seq}""');");
            }
        }

        private static readonly (string Table, string Prefix)[] RefTables =
        {
            ("Clients", "C"),
            ("Sites", "P"),
            ("Assets", "A"),
            ("SiteContacts", "PC"),
            ("Defects", "D"),
            ("ServiceReports", "SR"),
            ("MaintenanceSchedules", "MS"),
            ("ClientTasks", "T"),
        };

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, _) in RefTables)
            {
                migrationBuilder.Sql($@"DROP TRIGGER ""{table}_assign_fyre_ref"" ON ""{table}"";");
                migrationBuilder.Sql($@"DROP SEQUENCE ""{table}_FyreRef_seq"";");
            }
            migrationBuilder.Sql("DROP FUNCTION fyre_assign_ref();");

            migrationBuilder.DropIndex(
                name: "IX_Sites_FyreRef",
                table: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_SiteContacts_FyreRef",
                table: "SiteContacts");

            migrationBuilder.DropIndex(
                name: "IX_ServiceReports_FyreRef",
                table: "ServiceReports");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceSchedules_FyreRef",
                table: "MaintenanceSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Defects_FyreRef",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_FyreRef",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_Clients_FyreRef",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Assets_FyreRef",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "SiteContacts");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "MaintenanceSchedules");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "FyreRef",
                table: "Assets");
        }
    }
}
