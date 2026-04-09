using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UniqueAssetCatalogueName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove duplicate rows, keeping the one with the lowest Id
            migrationBuilder.Sql("""
                DELETE FROM "AssetCatalogue"
                WHERE "Id" NOT IN (
                    SELECT MIN("Id")
                    FROM "AssetCatalogue"
                    GROUP BY LOWER("Name")
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AssetCatalogue_Name",
                table: "AssetCatalogue",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssetCatalogue_Name",
                table: "AssetCatalogue");
        }
    }
}
