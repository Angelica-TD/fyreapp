using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickRemarksReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Reports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Remarks",
                type: "bigint",
                nullable: true);

            // Rows the file imports made already carry Uptick's id in ExternalId, so the mappers update them
            // rather than adding them again
            foreach (var table in new[] { "Remarks", "Reports" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_UptickId",
                table: "Reports",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_UptickId",
                table: "Remarks",
                column: "UptickId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reports_UptickId",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Remarks_UptickId",
                table: "Remarks");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Remarks");
        }
    }
}
