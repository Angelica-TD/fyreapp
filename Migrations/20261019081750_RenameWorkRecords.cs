using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <summary>
    /// Phase 4a (docs/uptick-migration.md §5.6): the work records take Uptick's names. Renamed, not rebuilt: rows,
    /// ids and FyreRefs stay as they are.
    /// </summary>
    public partial class RenameWorkRecords : Migration
    {
        private static readonly (string Old, string New)[] Tables =
        {
            ("Defects", "Remarks"),
            ("ServiceReports", "Reports"),
            ("RoutineOccurrences", "Routines"),
            ("PropertyRoutineServices", "RoutineServices"),
            ("TaskAssets", "ServiceTasks"),
        };

        // (table after the rename, old name, new name); the long ones were cut to 63 characters by Postgres
        private static readonly (string Table, string Old, string New)[] Constraints =
        {
            ("Remarks", "PK_Defects", "PK_Remarks"),
            ("Remarks", "FK_Defects_Assets_AssetId", "FK_Remarks_Assets_AssetId"),
            ("Remarks", "FK_Defects_ClientTasks_RepairTaskId", "FK_Remarks_ClientTasks_RepairTaskId"),
            ("Remarks", "FK_Defects_Properties_PropertyId", "FK_Remarks_Properties_PropertyId"),
            ("Remarks", "FK_Defects_RemarkTypes_RemarkTypeId", "FK_Remarks_RemarkTypes_RemarkTypeId"),
            ("Reports", "PK_ServiceReports", "PK_Reports"),
            ("Reports", "FK_ServiceReports_ClientTasks_ClientTaskId", "FK_Reports_ClientTasks_ClientTaskId"),
            ("Reports", "FK_ServiceReports_Properties_PropertyId", "FK_Reports_Properties_PropertyId"),
            ("Routines", "PK_RoutineOccurrences", "PK_Routines"),
            ("Routines", "FK_RoutineOccurrences_ClientTasks_ClientTaskId", "FK_Routines_ClientTasks_ClientTaskId"),
            ("Routines", "FK_RoutineOccurrences_MaintenanceSchedules_MaintenanceSchedule~", "FK_Routines_MaintenanceSchedules_MaintenanceScheduleId"),
            ("Routines", "FK_RoutineOccurrences_Properties_PropertyId", "FK_Routines_Properties_PropertyId"),
            ("Routines", "FK_RoutineOccurrences_PropertyRoutineServices_PropertyRoutineS~", "FK_Routines_RoutineServices_RoutineServiceId"),
            ("Routines", "FK_RoutineOccurrences_RoutineServiceLevels_RoutineServiceLevel~", "FK_Routines_RoutineServiceLevels_RoutineServiceLevelId"),
            ("RoutineServices", "PK_PropertyRoutineServices", "PK_RoutineServices"),
            ("RoutineServices", "FK_PropertyRoutineServices_Properties_PropertyId", "FK_RoutineServices_Properties_PropertyId"),
            ("RoutineServices", "FK_PropertyRoutineServices_RoutineServiceTypes_RoutineServiceT~", "FK_RoutineServices_RoutineServiceTypes_RoutineServiceTypeId"),
            ("ServiceTasks", "PK_TaskAssets", "PK_ServiceTasks"),
            ("ServiceTasks", "FK_TaskAssets_Assets_AssetId", "FK_ServiceTasks_Assets_AssetId"),
            ("ServiceTasks", "FK_TaskAssets_ClientTasks_ClientTaskId", "FK_ServiceTasks_ClientTasks_ClientTaskId"),
        };

        private static readonly (string Old, string New)[] Indexes =
        {
            ("IX_Defects_AssetId", "IX_Remarks_AssetId"),
            ("IX_Defects_ExternalId", "IX_Remarks_ExternalId"),
            ("IX_Defects_FyreRef", "IX_Remarks_FyreRef"),
            ("IX_Defects_PropertyId", "IX_Remarks_PropertyId"),
            ("IX_Defects_RemarkTypeId", "IX_Remarks_RemarkTypeId"),
            ("IX_Defects_RepairTaskExternalId", "IX_Remarks_RepairTaskExternalId"),
            ("IX_Defects_RepairTaskId", "IX_Remarks_RepairTaskId"),
            ("IX_ServiceReports_ClientTaskId", "IX_Reports_ClientTaskId"),
            ("IX_ServiceReports_ExternalId", "IX_Reports_ExternalId"),
            ("IX_ServiceReports_FyreRef", "IX_Reports_FyreRef"),
            ("IX_ServiceReports_PropertyId", "IX_Reports_PropertyId"),
            ("IX_ServiceReports_TaskExternalId", "IX_Reports_TaskExternalId"),
            ("IX_RoutineOccurrences_ClientTaskId", "IX_Routines_ClientTaskId"),
            ("IX_RoutineOccurrences_DueDate_Status", "IX_Routines_DueDate_Status"),
            ("IX_RoutineOccurrences_ExternalId", "IX_Routines_ExternalId"),
            ("IX_RoutineOccurrences_MaintenanceScheduleId", "IX_Routines_MaintenanceScheduleId"),
            ("IX_RoutineOccurrences_PropertyId", "IX_Routines_PropertyId"),
            ("IX_RoutineOccurrences_PropertyRoutineServiceId", "IX_Routines_RoutineServiceId"),
            ("IX_RoutineOccurrences_RoutineServiceLevelId", "IX_Routines_RoutineServiceLevelId"),
            ("IX_PropertyRoutineServices_PropertyId", "IX_RoutineServices_PropertyId"),
            ("IX_PropertyRoutineServices_RoutineServiceTypeId", "IX_RoutineServices_RoutineServiceTypeId"),
            ("IX_TaskAssets_AssetId", "IX_ServiceTasks_AssetId"),
            ("IX_TaskAssets_ClientTaskId_AssetId_Routine", "IX_ServiceTasks_ClientTaskId_AssetId_Routine"),
        };

        // The FyreRef triggers (AddFyreRefs) name their sequence as text: (old table, new table, prefix)
        private static readonly (string Old, string New, string Prefix)[] FyreRefs =
        {
            ("Defects", "Remarks", "FD"),
            ("ServiceReports", "Reports", "FSR"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PropertyRoutineServiceLevels_PropertyRoutineServices_Proper~",
                table: "PropertyRoutineServiceLevels");

            foreach (var (from, to) in Tables)
                migrationBuilder.RenameTable(name: from, newName: to);

            migrationBuilder.RenameColumn(name: "Routine", table: "Routines", newName: "Name");
            migrationBuilder.RenameColumn(name: "PropertyRoutineServiceId", table: "Routines", newName: "RoutineServiceId");
            // The levels join keeps its table name ("RoutineServiceLevels" is the levels themselves)
            migrationBuilder.RenameColumn(name: "PropertyRoutineServiceId", table: "PropertyRoutineServiceLevels", newName: "RoutineServiceId");
            migrationBuilder.RenameIndex(
                name: "IX_PropertyRoutineServiceLevels_PropertyRoutineServiceId",
                table: "PropertyRoutineServiceLevels",
                newName: "IX_PropertyRoutineServiceLevels_RoutineServiceId");

            Rename(migrationBuilder, up: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyRoutineServiceLevels_RoutineServices_RoutineService~",
                table: "PropertyRoutineServiceLevels",
                column: "RoutineServiceId",
                principalTable: "RoutineServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PropertyRoutineServiceLevels_RoutineServices_RoutineService~",
                table: "PropertyRoutineServiceLevels");

            Rename(migrationBuilder, up: false);

            migrationBuilder.RenameIndex(
                name: "IX_PropertyRoutineServiceLevels_RoutineServiceId",
                table: "PropertyRoutineServiceLevels",
                newName: "IX_PropertyRoutineServiceLevels_PropertyRoutineServiceId");
            migrationBuilder.RenameColumn(name: "RoutineServiceId", table: "PropertyRoutineServiceLevels", newName: "PropertyRoutineServiceId");
            migrationBuilder.RenameColumn(name: "RoutineServiceId", table: "Routines", newName: "PropertyRoutineServiceId");
            migrationBuilder.RenameColumn(name: "Name", table: "Routines", newName: "Routine");

            foreach (var (from, to) in Tables)
                migrationBuilder.RenameTable(name: to, newName: from);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyRoutineServiceLevels_PropertyRoutineServices_Proper~",
                table: "PropertyRoutineServiceLevels",
                column: "PropertyRoutineServiceId",
                principalTable: "PropertyRoutineServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        // Constraints, indexes, id sequences and FyreRef triggers, run while the tables have their new names
        private static void Rename(MigrationBuilder migrationBuilder, bool up)
        {
            foreach (var (table, oldName, newName) in Constraints)
                migrationBuilder.Sql(up
                    ? $"""ALTER TABLE "{table}" RENAME CONSTRAINT "{oldName}" TO "{newName}";"""
                    : $"""ALTER TABLE "{table}" RENAME CONSTRAINT "{newName}" TO "{oldName}";""");

            foreach (var (oldName, newName) in Indexes)
                migrationBuilder.Sql(up
                    ? $"""ALTER INDEX "{oldName}" RENAME TO "{newName}";"""
                    : $"""ALTER INDEX "{newName}" RENAME TO "{oldName}";""");

            foreach (var (oldTable, newTable) in Tables)
                migrationBuilder.Sql(up
                    ? $"""ALTER SEQUENCE "{oldTable}_Id_seq" RENAME TO "{newTable}_Id_seq";"""
                    : $"""ALTER SEQUENCE "{newTable}_Id_seq" RENAME TO "{oldTable}_Id_seq";""");

            foreach (var (oldTable, newTable, prefix) in FyreRefs)
            {
                var (from, to) = up ? (oldTable, newTable) : (newTable, oldTable);
                migrationBuilder.Sql($"""
                    ALTER SEQUENCE "{from}_FyreRef_seq" RENAME TO "{to}_FyreRef_seq";
                    DROP TRIGGER "{from}_assign_fyre_ref" ON "{newTable}";
                    CREATE TRIGGER "{to}_assign_fyre_ref" BEFORE INSERT ON "{newTable}"
                        FOR EACH ROW EXECUTE FUNCTION fyre_assign_ref('{prefix}', '"{to}_FyreRef_seq"');
                    """);
            }
        }
    }
}
