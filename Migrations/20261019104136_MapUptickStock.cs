using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Warehouses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "Stocks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Stocks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Stocks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Products",
                type: "bigint",
                nullable: true);

            // Products and warehouses the exports made carry Uptick's id in ExternalId, so the mappers update them
            foreach (var table in new[] { "Products", "Warehouses" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_UptickId",
                table: "Warehouses",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProductId",
                table: "Stocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_UptickId",
                table: "Stocks",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_WarehouseId",
                table: "Stocks",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_UptickId",
                table: "Products",
                column: "UptickId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Products_ProductId",
                table: "Stocks",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Warehouses_WarehouseId",
                table: "Stocks",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Products_ProductId",
                table: "Stocks");

            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Warehouses_WarehouseId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_UptickId",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_ProductId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_UptickId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_WarehouseId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Products_UptickId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Products");
        }
    }
}
