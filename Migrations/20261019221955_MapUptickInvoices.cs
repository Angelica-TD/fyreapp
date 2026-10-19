using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClientTaskId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceId",
                table: "CreditNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Total",
                table: "CreditNotes",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "CreditNotes",
                type: "bigint",
                nullable: true);

            // Invoices and credit notes the exports made carry Uptick's id in ExternalId, so the mappers update them
            foreach (var table in new[] { "Invoices", "CreditNotes" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClientTaskId",
                table: "Invoices",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PropertyId",
                table: "Invoices",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UptickId",
                table: "Invoices",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_UptickId",
                table: "CreditNotes",
                column: "UptickId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_Invoices_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_ClientTasks_ClientTaskId",
                table: "Invoices",
                column: "ClientTaskId",
                principalTable: "ClientTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Properties_PropertyId",
                table: "Invoices",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_Invoices_InvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_ClientTasks_ClientTaskId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Properties_PropertyId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ClientTaskId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PropertyId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_UptickId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_UptickId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "ClientTaskId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "Total",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "CreditNotes");
        }
    }
}
