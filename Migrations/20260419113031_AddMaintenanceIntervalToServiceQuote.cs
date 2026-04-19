using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceIntervalToServiceQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaintenanceIntervalId",
                table: "ServiceQuotes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuotes_MaintenanceIntervalId",
                table: "ServiceQuotes",
                column: "MaintenanceIntervalId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceQuotes_MaintenanceIntervals_MaintenanceIntervalId",
                table: "ServiceQuotes",
                column: "MaintenanceIntervalId",
                principalTable: "MaintenanceIntervals",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceQuotes_MaintenanceIntervals_MaintenanceIntervalId",
                table: "ServiceQuotes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceQuotes_MaintenanceIntervalId",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "MaintenanceIntervalId",
                table: "ServiceQuotes");
        }
    }
}
