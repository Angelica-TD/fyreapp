using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class EditAssetType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetAssetTypes_AssetTypes_TypesId",
                table: "AssetAssetTypes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AssetAssetTypes",
                table: "AssetAssetTypes");

            migrationBuilder.DropIndex(
                name: "IX_AssetAssetTypes_TypesId",
                table: "AssetAssetTypes");

            migrationBuilder.RenameColumn(
                name: "TypesId",
                table: "AssetAssetTypes",
                newName: "AssetTypesId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AssetAssetTypes",
                table: "AssetAssetTypes",
                columns: new[] { "AssetTypesId", "AssetsId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssetTypes_AssetsId",
                table: "AssetAssetTypes",
                column: "AssetsId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetAssetTypes_AssetTypes_AssetTypesId",
                table: "AssetAssetTypes",
                column: "AssetTypesId",
                principalTable: "AssetTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetAssetTypes_AssetTypes_AssetTypesId",
                table: "AssetAssetTypes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AssetAssetTypes",
                table: "AssetAssetTypes");

            migrationBuilder.DropIndex(
                name: "IX_AssetAssetTypes_AssetsId",
                table: "AssetAssetTypes");

            migrationBuilder.RenameColumn(
                name: "AssetTypesId",
                table: "AssetAssetTypes",
                newName: "TypesId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AssetAssetTypes",
                table: "AssetAssetTypes",
                columns: new[] { "AssetsId", "TypesId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssetTypes_TypesId",
                table: "AssetAssetTypes",
                column: "TypesId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetAssetTypes_AssetTypes_TypesId",
                table: "AssetAssetTypes",
                column: "TypesId",
                principalTable: "AssetTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
