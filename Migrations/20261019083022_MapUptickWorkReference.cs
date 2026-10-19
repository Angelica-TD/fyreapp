using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickWorkReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "RoutineServiceTypes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "RoutineServiceLevels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "RemarkTypes",
                type: "bigint",
                nullable: true);

            // Rows the file imports made already carry Uptick's id in ExternalId, so the mappers update them
            // rather than adding them again
            foreach (var table in new[] { "RoutineServiceTypes", "RoutineServiceLevels", "RemarkTypes" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceTypes_UptickId",
                table: "RoutineServiceTypes",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceLevels_UptickId",
                table: "RoutineServiceLevels",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemarkTypes_UptickId",
                table: "RemarkTypes",
                column: "UptickId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoutineServiceTypes_UptickId",
                table: "RoutineServiceTypes");

            migrationBuilder.DropIndex(
                name: "IX_RoutineServiceLevels_UptickId",
                table: "RoutineServiceLevels");

            migrationBuilder.DropIndex(
                name: "IX_RemarkTypes_UptickId",
                table: "RemarkTypes");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "RoutineServiceTypes");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "RoutineServiceLevels");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "RemarkTypes");
        }
    }
}
