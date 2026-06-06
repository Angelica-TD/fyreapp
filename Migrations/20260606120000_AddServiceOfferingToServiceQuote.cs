using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOfferingToServiceQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServiceOfferingId",
                table: "ServiceQuotes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuotes_ServiceOfferingId",
                table: "ServiceQuotes",
                column: "ServiceOfferingId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceQuotes_ServiceOfferings_ServiceOfferingId",
                table: "ServiceQuotes",
                column: "ServiceOfferingId",
                principalTable: "ServiceOfferings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceQuotes_ServiceOfferings_ServiceOfferingId",
                table: "ServiceQuotes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceQuotes_ServiceOfferingId",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "ServiceOfferingId",
                table: "ServiceQuotes");
        }
    }
}
