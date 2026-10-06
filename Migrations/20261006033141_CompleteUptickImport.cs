using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class CompleteUptickImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_Name",
                table: "Clients");

            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "Sites",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPlaceholder",
                table: "Sites",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Sites",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "SiteContacts",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "ServiceReports",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Defects",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPlaceholder",
                table: "Clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Clients",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Assets",
                type: "json",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Name",
                table: "Clients",
                column: "Name");

            // Names no longer need to be unique: undo the "Name (Uptick <ID>)" renames from earlier imports
            migrationBuilder.Sql(@"
UPDATE ""Clients""
SET ""Name"" = left(""Name"", length(""Name"") - length(' (Uptick ' || ""ExternalId"" || ')'))
WHERE ""ExternalId"" IS NOT NULL
  AND right(""Name"", length(' (Uptick ' || ""ExternalId"" || ')')) = ' (Uptick ' || ""ExternalId"" || ')';");

            // Uptick itself uses P- (properties), T- (tasks), Q- and R- refs, so FyreApp refs get an F
            // in front to never look like an Uptick one: C-1001 -> FC-1001, P-1001 -> FP-1001, ...
            foreach (var (table, prefix) in RefTables)
                SetRefPrefix(migrationBuilder, table, "F" + prefix);
        }

        // Same tables and prefixes as AddFyreRefs
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

        // Re-creates the table's ref trigger with `prefix` and rewrites existing refs to match
        private static void SetRefPrefix(MigrationBuilder migrationBuilder, string table, string prefix)
        {
            migrationBuilder.Sql($@"DROP TRIGGER ""{table}_assign_fyre_ref"" ON ""{table}"";");
            migrationBuilder.Sql($@"
CREATE TRIGGER ""{table}_assign_fyre_ref""
BEFORE INSERT ON ""{table}""
FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('{prefix}', '""{table}_FyreRef_seq""');");
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""FyreRef"" = '{prefix}' || substring(""FyreRef"" from position('-' in ""FyreRef""))
WHERE ""FyreRef"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, prefix) in RefTables)
                SetRefPrefix(migrationBuilder, table, prefix);

            migrationBuilder.DropIndex(
                name: "IX_Clients_Name",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "IsPlaceholder",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "SiteContacts");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "IsPlaceholder",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Assets");

            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "Sites",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Name",
                table: "Clients",
                column: "Name",
                unique: true);
        }
    }
}
