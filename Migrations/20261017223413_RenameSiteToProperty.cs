using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <summary>
    /// Site / SiteContact become Property / PropertyContact, Uptick's names (docs/uptick-migration.md §5.6, phase 3):
    /// tables, foreign keys, indexes, sequences and FyreRef triggers renamed, no data copied.
    /// </summary>
    public partial class RenameSiteToProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Sites_SiteId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientTasks_Sites_SiteId",
                table: "ClientTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_Sites_SiteId",
                table: "Defects");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceSchedules_Sites_SiteId",
                table: "MaintenanceSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_PropertyRoutineServices_Sites_SiteId",
                table: "PropertyRoutineServices");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotes_Sites_SiteId",
                table: "Quotes");

            migrationBuilder.DropForeignKey(
                name: "FK_RoutineOccurrences_Sites_SiteId",
                table: "RoutineOccurrences");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceQuotes_Sites_SiteId",
                table: "ServiceQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceReports_Sites_SiteId",
                table: "ServiceReports");

            migrationBuilder.DropForeignKey(
                name: "FK_TimelineEvents_Sites_SiteId",
                table: "TimelineEvents");

            // Renamed, not rebuilt: rows, ids and FyreRefs stay as they are
            migrationBuilder.RenameTable(name: "Sites", newName: "Properties");
            migrationBuilder.RenameTable(name: "SiteContacts", newName: "PropertyContacts");
            migrationBuilder.RenameColumn(name: "SiteId", table: "PropertyContacts", newName: "PropertyId");
            migrationBuilder.Sql("""
                ALTER TABLE "Properties" RENAME CONSTRAINT "PK_Sites" TO "PK_Properties";
                ALTER TABLE "Properties" RENAME CONSTRAINT "FK_Sites_Clients_ClientId" TO "FK_Properties_Clients_ClientId";
                ALTER TABLE "PropertyContacts" RENAME CONSTRAINT "PK_SiteContacts" TO "PK_PropertyContacts";
                ALTER TABLE "PropertyContacts" RENAME CONSTRAINT "FK_SiteContacts_Sites_SiteId" TO "FK_PropertyContacts_Properties_PropertyId";
                ALTER INDEX "IX_Sites_ClientId" RENAME TO "IX_Properties_ClientId";
                ALTER INDEX "IX_Sites_ExternalId" RENAME TO "IX_Properties_ExternalId";
                ALTER INDEX "IX_Sites_FyreRef" RENAME TO "IX_Properties_FyreRef";
                ALTER INDEX "IX_SiteContacts_ExternalId" RENAME TO "IX_PropertyContacts_ExternalId";
                ALTER INDEX "IX_SiteContacts_FyreRef" RENAME TO "IX_PropertyContacts_FyreRef";
                ALTER INDEX "IX_SiteContacts_SiteId" RENAME TO "IX_PropertyContacts_PropertyId";
                ALTER SEQUENCE "Sites_Id_seq" RENAME TO "Properties_Id_seq";
                ALTER SEQUENCE "SiteContacts_Id_seq" RENAME TO "PropertyContacts_Id_seq";
                ALTER SEQUENCE "Sites_FyreRef_seq" RENAME TO "Properties_FyreRef_seq";
                ALTER SEQUENCE "SiteContacts_FyreRef_seq" RENAME TO "PropertyContacts_FyreRef_seq";
                -- The FyreRef triggers (AddFyreRefs) name their sequence as text
                DROP TRIGGER "Sites_assign_fyre_ref" ON "Properties";
                CREATE TRIGGER "Properties_assign_fyre_ref" BEFORE INSERT ON "Properties"
                    FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('FP', '"Properties_FyreRef_seq"');
                DROP TRIGGER "SiteContacts_assign_fyre_ref" ON "PropertyContacts";
                CREATE TRIGGER "PropertyContacts_assign_fyre_ref" BEFORE INSERT ON "PropertyContacts"
                    FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('FPC', '"PropertyContacts_FyreRef_seq"');
                """);

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "TimelineEvents",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_TimelineEvents_SiteId_WhenUtc",
                table: "TimelineEvents",
                newName: "IX_TimelineEvents_PropertyId_WhenUtc");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "ServiceReports",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceReports_SiteId",
                table: "ServiceReports",
                newName: "IX_ServiceReports_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "ServiceQuotes",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceQuotes_SiteId",
                table: "ServiceQuotes",
                newName: "IX_ServiceQuotes_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "RoutineOccurrences",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_RoutineOccurrences_SiteId",
                table: "RoutineOccurrences",
                newName: "IX_RoutineOccurrences_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "Quotes",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_Quotes_SiteId",
                table: "Quotes",
                newName: "IX_Quotes_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "PropertyRoutineServices",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_PropertyRoutineServices_SiteId",
                table: "PropertyRoutineServices",
                newName: "IX_PropertyRoutineServices_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "MaintenanceSchedules",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_MaintenanceSchedules_SiteId",
                table: "MaintenanceSchedules",
                newName: "IX_MaintenanceSchedules_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "Defects",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_Defects_SiteId",
                table: "Defects",
                newName: "IX_Defects_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "ClientTasks",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_ClientTasks_SiteId",
                table: "ClientTasks",
                newName: "IX_ClientTasks_PropertyId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "Assets",
                newName: "PropertyId");

            migrationBuilder.RenameIndex(
                name: "IX_Assets_SiteId",
                table: "Assets",
                newName: "IX_Assets_PropertyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Properties_PropertyId",
                table: "Assets",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ClientTasks_Properties_PropertyId",
                table: "ClientTasks",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_Properties_PropertyId",
                table: "Defects",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceSchedules_Properties_PropertyId",
                table: "MaintenanceSchedules",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyRoutineServices_Properties_PropertyId",
                table: "PropertyRoutineServices",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotes_Properties_PropertyId",
                table: "Quotes",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoutineOccurrences_Properties_PropertyId",
                table: "RoutineOccurrences",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceQuotes_Properties_PropertyId",
                table: "ServiceQuotes",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceReports_Properties_PropertyId",
                table: "ServiceReports",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimelineEvents_Properties_PropertyId",
                table: "TimelineEvents",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Properties_PropertyId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientTasks_Properties_PropertyId",
                table: "ClientTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_Properties_PropertyId",
                table: "Defects");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceSchedules_Properties_PropertyId",
                table: "MaintenanceSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_PropertyRoutineServices_Properties_PropertyId",
                table: "PropertyRoutineServices");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotes_Properties_PropertyId",
                table: "Quotes");

            migrationBuilder.DropForeignKey(
                name: "FK_RoutineOccurrences_Properties_PropertyId",
                table: "RoutineOccurrences");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceQuotes_Properties_PropertyId",
                table: "ServiceQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceReports_Properties_PropertyId",
                table: "ServiceReports");

            migrationBuilder.DropForeignKey(
                name: "FK_TimelineEvents_Properties_PropertyId",
                table: "TimelineEvents");

            // Renamed, not rebuilt: rows, ids and FyreRefs stay as they are
            migrationBuilder.RenameTable(name: "Properties", newName: "Sites");
            migrationBuilder.RenameTable(name: "PropertyContacts", newName: "SiteContacts");
            migrationBuilder.RenameColumn(name: "PropertyId", table: "SiteContacts", newName: "SiteId");
            migrationBuilder.Sql("""
                ALTER TABLE "Sites" RENAME CONSTRAINT "PK_Properties" TO "PK_Sites";
                ALTER TABLE "Sites" RENAME CONSTRAINT "FK_Properties_Clients_ClientId" TO "FK_Sites_Clients_ClientId";
                ALTER TABLE "SiteContacts" RENAME CONSTRAINT "PK_PropertyContacts" TO "PK_SiteContacts";
                ALTER TABLE "SiteContacts" RENAME CONSTRAINT "FK_PropertyContacts_Properties_PropertyId" TO "FK_SiteContacts_Sites_SiteId";
                ALTER INDEX "IX_Properties_ClientId" RENAME TO "IX_Sites_ClientId";
                ALTER INDEX "IX_Properties_ExternalId" RENAME TO "IX_Sites_ExternalId";
                ALTER INDEX "IX_Properties_FyreRef" RENAME TO "IX_Sites_FyreRef";
                ALTER INDEX "IX_PropertyContacts_ExternalId" RENAME TO "IX_SiteContacts_ExternalId";
                ALTER INDEX "IX_PropertyContacts_FyreRef" RENAME TO "IX_SiteContacts_FyreRef";
                ALTER INDEX "IX_PropertyContacts_PropertyId" RENAME TO "IX_SiteContacts_SiteId";
                ALTER SEQUENCE "Properties_Id_seq" RENAME TO "Sites_Id_seq";
                ALTER SEQUENCE "PropertyContacts_Id_seq" RENAME TO "SiteContacts_Id_seq";
                ALTER SEQUENCE "Properties_FyreRef_seq" RENAME TO "Sites_FyreRef_seq";
                ALTER SEQUENCE "PropertyContacts_FyreRef_seq" RENAME TO "SiteContacts_FyreRef_seq";
                -- The FyreRef triggers (AddFyreRefs) name their sequence as text
                DROP TRIGGER "Properties_assign_fyre_ref" ON "Sites";
                CREATE TRIGGER "Sites_assign_fyre_ref" BEFORE INSERT ON "Sites"
                    FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('FP', '"Sites_FyreRef_seq"');
                DROP TRIGGER "PropertyContacts_assign_fyre_ref" ON "SiteContacts";
                CREATE TRIGGER "SiteContacts_assign_fyre_ref" BEFORE INSERT ON "SiteContacts"
                    FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('FPC', '"SiteContacts_FyreRef_seq"');
                """);

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "TimelineEvents",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_TimelineEvents_PropertyId_WhenUtc",
                table: "TimelineEvents",
                newName: "IX_TimelineEvents_SiteId_WhenUtc");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "ServiceReports",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceReports_PropertyId",
                table: "ServiceReports",
                newName: "IX_ServiceReports_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "ServiceQuotes",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceQuotes_PropertyId",
                table: "ServiceQuotes",
                newName: "IX_ServiceQuotes_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "RoutineOccurrences",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_RoutineOccurrences_PropertyId",
                table: "RoutineOccurrences",
                newName: "IX_RoutineOccurrences_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "Quotes",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_Quotes_PropertyId",
                table: "Quotes",
                newName: "IX_Quotes_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "PropertyRoutineServices",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_PropertyRoutineServices_PropertyId",
                table: "PropertyRoutineServices",
                newName: "IX_PropertyRoutineServices_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "MaintenanceSchedules",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_MaintenanceSchedules_PropertyId",
                table: "MaintenanceSchedules",
                newName: "IX_MaintenanceSchedules_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "Defects",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_Defects_PropertyId",
                table: "Defects",
                newName: "IX_Defects_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "ClientTasks",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_ClientTasks_PropertyId",
                table: "ClientTasks",
                newName: "IX_ClientTasks_SiteId");

            migrationBuilder.RenameColumn(
                name: "PropertyId",
                table: "Assets",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_Assets_PropertyId",
                table: "Assets",
                newName: "IX_Assets_SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Sites_SiteId",
                table: "Assets",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ClientTasks_Sites_SiteId",
                table: "ClientTasks",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_Sites_SiteId",
                table: "Defects",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceSchedules_Sites_SiteId",
                table: "MaintenanceSchedules",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyRoutineServices_Sites_SiteId",
                table: "PropertyRoutineServices",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotes_Sites_SiteId",
                table: "Quotes",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoutineOccurrences_Sites_SiteId",
                table: "RoutineOccurrences",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceQuotes_Sites_SiteId",
                table: "ServiceQuotes",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceReports_Sites_SiteId",
                table: "ServiceReports",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimelineEvents_Sites_SiteId",
                table: "TimelineEvents",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
