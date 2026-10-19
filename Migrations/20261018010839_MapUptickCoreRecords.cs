using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickCoreRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "UptickUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "PropertyContacts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ref",
                table: "Properties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Properties",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Clients",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "ClientContacts",
                type: "bigint",
                nullable: true);

            // Rows the file imports made already carry Uptick's id in ExternalId (a property its ref, tied to its
            // id by the raw copy), so the mappers update them rather than adding them again
            foreach (var table in new[] { "Clients", "ClientContacts", "PropertyContacts", "UptickUsers" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.Sql(@"
UPDATE ""Properties"" SET ""Ref"" = ""ExternalId"" WHERE ""ExternalId"" <> '';

UPDATE ""Properties"" p SET ""UptickId"" = r.""UptickId""::bigint
FROM (
    SELECT DISTINCT ON (""Attributes""->>'ref') ""Attributes""->>'ref' AS ref, ""UptickId""
    FROM ""UptickRecords""
    WHERE ""Resource"" = 'properties' AND ""UptickId"" ~ '^[0-9]{1,18}$' AND coalesce(""Attributes""->>'ref', '') <> ''
    ORDER BY ""Attributes""->>'ref', ""DeletedUtc"" IS NOT NULL, ""ChangedUtc"" DESC
) r
WHERE p.""ExternalId"" = r.ref;");

            migrationBuilder.CreateIndex(
                name: "IX_UptickUsers_UptickId",
                table: "UptickUsers",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyContacts_UptickId",
                table: "PropertyContacts",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_UptickId",
                table: "Properties",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_UptickId",
                table: "Clients",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_UptickId",
                table: "ClientContacts",
                column: "UptickId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UptickUsers_UptickId",
                table: "UptickUsers");

            migrationBuilder.DropIndex(
                name: "IX_PropertyContacts_UptickId",
                table: "PropertyContacts");

            migrationBuilder.DropIndex(
                name: "IX_Properties_UptickId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Clients_UptickId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_ClientContacts_UptickId",
                table: "ClientContacts");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "UptickUsers");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "PropertyContacts");

            migrationBuilder.DropColumn(
                name: "Ref",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "ClientContacts");
        }
    }
}
