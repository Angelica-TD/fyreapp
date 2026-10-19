using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "AssetTypeVariants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "AssetTypes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastServicedById",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Assets",
                type: "bigint",
                nullable: true);

            // Rows the file imports made already carry Uptick's id in ExternalId, so the mappers update them
            // rather than adding them again
            foreach (var table in new[] { "Assets", "AssetTypes", "AssetTypeVariants" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypeVariants_UptickId",
                table: "AssetTypeVariants",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypes_UptickId",
                table: "AssetTypes",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_LastServicedById",
                table: "Assets",
                column: "LastServicedById");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_UptickId",
                table: "Assets",
                column: "UptickId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_UptickUsers_LastServicedById",
                table: "Assets",
                column: "LastServicedById",
                principalTable: "UptickUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_UptickUsers_LastServicedById",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_AssetTypeVariants_UptickId",
                table: "AssetTypeVariants");

            migrationBuilder.DropIndex(
                name: "IX_AssetTypes_UptickId",
                table: "AssetTypes");

            migrationBuilder.DropIndex(
                name: "IX_Assets_LastServicedById",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_UptickId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "AssetTypeVariants");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "LastServicedById",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Assets");
        }
    }
}
