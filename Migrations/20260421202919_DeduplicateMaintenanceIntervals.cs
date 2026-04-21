using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class DeduplicateMaintenanceIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove duplicate rows inserted by both DbInitialiser and the seed migration.
            // Keep the row with the lowest Id for each Name (that's the one existing FKs reference).
            migrationBuilder.Sql(@"
                DELETE FROM ""MaintenanceIntervals""
                WHERE ""Id"" NOT IN (
                    SELECT MIN(""Id"") FROM ""MaintenanceIntervals"" GROUP BY ""Name""
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceIntervals_Name",
                table: "MaintenanceIntervals",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaintenanceIntervals_Name",
                table: "MaintenanceIntervals");
        }
    }
}
