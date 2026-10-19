using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickRoutines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "RoutineServices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Routines",
                type: "bigint",
                nullable: true);

            // Routines the routines export made carry Uptick's id in ExternalId, so the mapper updates them
            migrationBuilder.Sql(@"
UPDATE ""Routines"" SET ""UptickId"" = ""ExternalId""::bigint WHERE ""ExternalId"" ~ '^[0-9]{1,18}$';");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServices_UptickId",
                table: "RoutineServices",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routines_UptickId",
                table: "Routines",
                column: "UptickId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoutineServices_UptickId",
                table: "RoutineServices");

            migrationBuilder.DropIndex(
                name: "IX_Routines_UptickId",
                table: "Routines");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "RoutineServices");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Routines");
        }
    }
}
