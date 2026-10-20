using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickQuoteInvoiceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefectQuoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    DefectQuoteId = table.Column<int>(type: "integer", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    AssetId = table.Column<int>(type: "integer", nullable: true),
                    RemarkId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    CostPrice = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Tax = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefectQuoteLines_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DefectQuoteLines_DefectQuotes_DefectQuoteId",
                        column: x => x.DefectQuoteId,
                        principalTable: "DefectQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DefectQuoteLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DefectQuoteLines_Remarks_RemarkId",
                        column: x => x.RemarkId,
                        principalTable: "Remarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Tax = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    AccountCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RemarkEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    RemarkId = table.Column<int>(type: "integer", nullable: false),
                    Event = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    WhenUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UptickUserId = table.Column<int>(type: "integer", nullable: true),
                    ClientTaskId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemarkEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemarkEvents_ClientTasks_ClientTaskId",
                        column: x => x.ClientTaskId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RemarkEvents_Remarks_RemarkId",
                        column: x => x.RemarkId,
                        principalTable: "Remarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RemarkEvents_UptickUsers_UptickUserId",
                        column: x => x.UptickUserId,
                        principalTable: "UptickUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ServiceQuoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UptickId = table.Column<long>(type: "bigint", nullable: false),
                    ServiceQuoteId = table.Column<int>(type: "integer", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    RoutineServiceTypeId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    CostPrice = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Tax = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    EstimatedDuration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceQuoteLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ServiceQuoteLines_RoutineServiceTypes_RoutineServiceTypeId",
                        column: x => x.RoutineServiceTypeId,
                        principalTable: "RoutineServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ServiceQuoteLines_ServiceQuotes_ServiceQuoteId",
                        column: x => x.ServiceQuoteId,
                        principalTable: "ServiceQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuoteLines_AssetId",
                table: "DefectQuoteLines",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuoteLines_DefectQuoteId",
                table: "DefectQuoteLines",
                column: "DefectQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuoteLines_ProductId",
                table: "DefectQuoteLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuoteLines_RemarkId",
                table: "DefectQuoteLines",
                column: "RemarkId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuoteLines_UptickId",
                table: "DefectQuoteLines",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_ProductId",
                table: "InvoiceLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_UptickId",
                table: "InvoiceLines",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemarkEvents_ClientTaskId",
                table: "RemarkEvents",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RemarkEvents_RemarkId",
                table: "RemarkEvents",
                column: "RemarkId");

            migrationBuilder.CreateIndex(
                name: "IX_RemarkEvents_UptickId",
                table: "RemarkEvents",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemarkEvents_UptickUserId",
                table: "RemarkEvents",
                column: "UptickUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuoteLines_Kind_UptickId",
                table: "ServiceQuoteLines",
                columns: new[] { "Kind", "UptickId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuoteLines_ProductId",
                table: "ServiceQuoteLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuoteLines_RoutineServiceTypeId",
                table: "ServiceQuoteLines",
                column: "RoutineServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuoteLines_ServiceQuoteId",
                table: "ServiceQuoteLines",
                column: "ServiceQuoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DefectQuoteLines");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "RemarkEvents");

            migrationBuilder.DropTable(
                name: "ServiceQuoteLines");
        }
    }
}
