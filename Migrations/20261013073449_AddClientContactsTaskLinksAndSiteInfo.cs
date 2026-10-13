using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddClientContactsTaskLinksAndSiteInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessCode",
                table: "Sites",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessNote",
                table: "Sites",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessProcedure",
                table: "Sites",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingRequirements",
                table: "Sites",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InactiveDate",
                table: "Sites",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "Sites",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Sites",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Sites",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyRequirements",
                table: "Sites",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceRequirements",
                table: "Sites",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientTaskId",
                table: "ServiceReports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskExternalId",
                table: "ServiceReports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairTaskExternalId",
                table: "Defects",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepairTaskId",
                table: "Defects",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FyreRef = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientContacts_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReports_ClientTaskId",
                table: "ServiceReports",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReports_TaskExternalId",
                table: "ServiceReports",
                column: "TaskExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_RepairTaskExternalId",
                table: "Defects",
                column: "RepairTaskExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_RepairTaskId",
                table: "Defects",
                column: "RepairTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_ClientId",
                table: "ClientContacts",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_ExternalId",
                table: "ClientContacts",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_FyreRef",
                table: "ClientContacts",
                column: "FyreRef",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_ClientTasks_RepairTaskId",
                table: "Defects",
                column: "RepairTaskId",
                principalTable: "ClientTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceReports_ClientTasks_ClientTaskId",
                table: "ServiceReports",
                column: "ClientTaskId",
                principalTable: "ClientTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Client contacts get FyreApp refs like the other records (function from AddFyreRefs)
            migrationBuilder.Sql(@"CREATE SEQUENCE ""ClientContacts_FyreRef_seq"" START 1001;");
            migrationBuilder.Sql(@"
CREATE TRIGGER ""ClientContacts_assign_fyre_ref""
BEFORE INSERT ON ""ClientContacts""
FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('CC', '""ClientContacts_FyreRef_seq""');");

            // Fill the new columns from the Uptick rows already stored on imported records. Re-imports skip
            // rows that haven't changed in Uptick, so they wouldn't fill them in. Uptick times are Brisbane time.
            migrationBuilder.Sql(@"
UPDATE ""Sites"" SET
    ""AccessProcedure"" = left(""UptickData""->>'Access Procedure', 20),
    ""AccessCode"" = left(""UptickData""->>'Access Code', 100),
    ""AccessNote"" = ""UptickData""->>'Access Note',
    ""InternalNote"" = ""UptickData""->>'Internal Note',
    ""ServiceRequirements"" = ""UptickData""->>'Service Requirements',
    ""SafetyRequirements"" = ""UptickData""->>'Safety Requirements',
    ""BillingRequirements"" = ""UptickData""->>'Billing Requirements',
    ""Latitude"" = CASE WHEN ""UptickData""->>'Address Lat' ~ '^-?[0-9]+(\.[0-9]+)?$'
                       THEN (""UptickData""->>'Address Lat')::double precision END,
    ""Longitude"" = CASE WHEN ""UptickData""->>'Address Lng' ~ '^-?[0-9]+(\.[0-9]+)?$'
                        THEN (""UptickData""->>'Address Lng')::double precision END,
    ""InactiveDate"" = CASE WHEN ""UptickData""->>'Inactive/Onhold Date' ~ '^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$'
                           THEN (""UptickData""->>'Inactive/Onhold Date')::timestamp AT TIME ZONE 'Australia/Brisbane' END
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""ServiceReports"" SET ""TaskExternalId"" = left(""UptickData""->>'Task Id', 64)
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""Defects"" SET ""RepairTaskExternalId"" = left(""UptickData""->>'Repair Task ID', 64)
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""ServiceReports"" r SET ""ClientTaskId"" = t.""Id""
FROM ""ClientTasks"" t
WHERE r.""TaskExternalId"" IS NOT NULL AND t.""ExternalId"" = r.""TaskExternalId"";");

            migrationBuilder.Sql(@"
UPDATE ""Defects"" d SET ""RepairTaskId"" = t.""Id""
FROM ""ClientTasks"" t
WHERE d.""RepairTaskExternalId"" IS NOT NULL AND t.""ExternalId"" = d.""RepairTaskExternalId"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Defects_ClientTasks_RepairTaskId",
                table: "Defects");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceReports_ClientTasks_ClientTaskId",
                table: "ServiceReports");

            migrationBuilder.DropTable(
                name: "ClientContacts");

            migrationBuilder.Sql(@"DROP SEQUENCE ""ClientContacts_FyreRef_seq"";");

            migrationBuilder.DropIndex(
                name: "IX_ServiceReports_ClientTaskId",
                table: "ServiceReports");

            migrationBuilder.DropIndex(
                name: "IX_ServiceReports_TaskExternalId",
                table: "ServiceReports");

            migrationBuilder.DropIndex(
                name: "IX_Defects_RepairTaskExternalId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_RepairTaskId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "AccessCode",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "AccessNote",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "AccessProcedure",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "BillingRequirements",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "InactiveDate",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "SafetyRequirements",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ServiceRequirements",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ClientTaskId",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "TaskExternalId",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "RepairTaskExternalId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "RepairTaskId",
                table: "Defects");
        }
    }
}
