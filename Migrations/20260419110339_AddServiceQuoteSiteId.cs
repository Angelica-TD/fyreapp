using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceQuoteSiteId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "ServiceQuotes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuotes_SiteId",
                table: "ServiceQuotes",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceQuotes_Sites_SiteId",
                table: "ServiceQuotes",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceQuotes_Sites_SiteId",
                table: "ServiceQuotes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceQuotes_SiteId",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ServiceQuotes");
        }
    }
}
