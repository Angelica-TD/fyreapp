using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickReportItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    AssetId = table.Column<int>(type: "integer", nullable: true),
                    Label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Make = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Size = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Standard = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Compliant = table.Column<bool>(type: "boolean", nullable: true),
                    InspectedDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ServicedDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    BaseDate = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportItems_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReportItems_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportItems_AssetId",
                table: "ReportItems",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportItems_ReportId",
                table: "ReportItems",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportItems_UptickId",
                table: "ReportItems",
                column: "UptickId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportItems");
        }
    }
}
