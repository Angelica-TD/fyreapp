using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillingCardId",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BillingCardId",
                table: "Clients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "BillingContracts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "BillingContracts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BillingContractId",
                table: "BillingContractLineItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "BillingContractLineItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "BillingCards",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "BillingCards",
                type: "bigint",
                nullable: true);

            // Billing records the exports made carry Uptick's id in ExternalId, so the mappers update them
            foreach (var table in new[] { "BillingCards", "BillingContracts", "BillingContractLineItems" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_BillingCardId",
                table: "Properties",
                column: "BillingCardId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_BillingCardId",
                table: "Clients",
                column: "BillingCardId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingContracts_PropertyId",
                table: "BillingContracts",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingContracts_UptickId",
                table: "BillingContracts",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingContractLineItems_BillingContractId",
                table: "BillingContractLineItems",
                column: "BillingContractId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingContractLineItems_UptickId",
                table: "BillingContractLineItems",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCards_UptickId",
                table: "BillingCards",
                column: "UptickId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingContractLineItems_BillingContracts_BillingContractId",
                table: "BillingContractLineItems",
                column: "BillingContractId",
                principalTable: "BillingContracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingContracts_Properties_PropertyId",
                table: "BillingContracts",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Clients_BillingCards_BillingCardId",
                table: "Clients",
                column: "BillingCardId",
                principalTable: "BillingCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_BillingCards_BillingCardId",
                table: "Properties",
                column: "BillingCardId",
                principalTable: "BillingCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillingContractLineItems_BillingContracts_BillingContractId",
                table: "BillingContractLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_BillingContracts_Properties_PropertyId",
                table: "BillingContracts");

            migrationBuilder.DropForeignKey(
                name: "FK_Clients_BillingCards_BillingCardId",
                table: "Clients");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_BillingCards_BillingCardId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_BillingCardId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Clients_BillingCardId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_BillingContracts_PropertyId",
                table: "BillingContracts");

            migrationBuilder.DropIndex(
                name: "IX_BillingContracts_UptickId",
                table: "BillingContracts");

            migrationBuilder.DropIndex(
                name: "IX_BillingContractLineItems_BillingContractId",
                table: "BillingContractLineItems");

            migrationBuilder.DropIndex(
                name: "IX_BillingContractLineItems_UptickId",
                table: "BillingContractLineItems");

            migrationBuilder.DropIndex(
                name: "IX_BillingCards_UptickId",
                table: "BillingCards");

            migrationBuilder.DropColumn(
                name: "BillingCardId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "BillingCardId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "BillingContracts");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "BillingContracts");

            migrationBuilder.DropColumn(
                name: "BillingContractId",
                table: "BillingContractLineItems");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "BillingContractLineItems");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "BillingCards");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "BillingCards");
        }
    }
}
