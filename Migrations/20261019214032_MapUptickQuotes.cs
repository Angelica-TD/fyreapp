using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "ServiceQuotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "ServiceQuotes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefectQuoteId",
                table: "Remarks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientTaskId",
                table: "DefectQuotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "DefectQuotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "DefectQuotes",
                type: "bigint",
                nullable: true);

            // Quotes the exports made carry Uptick's id in ExternalId, so the mappers update them
            foreach (var table in new[] { "DefectQuotes", "ServiceQuotes" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuotes_PropertyId",
                table: "ServiceQuotes",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuotes_UptickId",
                table: "ServiceQuotes",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_DefectQuoteId",
                table: "Remarks",
                column: "DefectQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuotes_ClientTaskId",
                table: "DefectQuotes",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuotes_PropertyId",
                table: "DefectQuotes",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuotes_UptickId",
                table: "DefectQuotes",
                column: "UptickId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DefectQuotes_ClientTasks_ClientTaskId",
                table: "DefectQuotes",
                column: "ClientTaskId",
                principalTable: "ClientTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DefectQuotes_Properties_PropertyId",
                table: "DefectQuotes",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Remarks_DefectQuotes_DefectQuoteId",
                table: "Remarks",
                column: "DefectQuoteId",
                principalTable: "DefectQuotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceQuotes_Properties_PropertyId",
                table: "ServiceQuotes",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DefectQuotes_ClientTasks_ClientTaskId",
                table: "DefectQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_DefectQuotes_Properties_PropertyId",
                table: "DefectQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Remarks_DefectQuotes_DefectQuoteId",
                table: "Remarks");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceQuotes_Properties_PropertyId",
                table: "ServiceQuotes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceQuotes_PropertyId",
                table: "ServiceQuotes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceQuotes_UptickId",
                table: "ServiceQuotes");

            migrationBuilder.DropIndex(
                name: "IX_Remarks_DefectQuoteId",
                table: "Remarks");

            migrationBuilder.DropIndex(
                name: "IX_DefectQuotes_ClientTaskId",
                table: "DefectQuotes");

            migrationBuilder.DropIndex(
                name: "IX_DefectQuotes_PropertyId",
                table: "DefectQuotes");

            migrationBuilder.DropIndex(
                name: "IX_DefectQuotes_UptickId",
                table: "DefectQuotes");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "DefectQuoteId",
                table: "Remarks");

            migrationBuilder.DropColumn(
                name: "ClientTaskId",
                table: "DefectQuotes");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "DefectQuotes");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "DefectQuotes");
        }
    }
}
