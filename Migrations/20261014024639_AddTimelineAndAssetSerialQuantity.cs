using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineAndAssetSerialQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Quantity",
                table: "Assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Serial",
                table: "Assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // From the Uptick rows already stored (re-imports skip unchanged rows, so wouldn't fill them in, and
            // would otherwise log "Serial was changed to ..." on the asset's Timeline when the row next changes)
            migrationBuilder.Sql(@"
UPDATE ""Assets"" SET
    ""Serial"" = left(nullif(btrim(""UptickData""->>'Inspection Ref'), ''), 100),
    ""Quantity"" = left(nullif(btrim(""UptickData""->>'Quantity'), ''), 100)
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "TimelineEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SiteId = table.Column<int>(type: "integer", nullable: true),
                    AssetId = table.Column<int>(type: "integer", nullable: true),
                    WhenUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsNote = table.Column<bool>(type: "boolean", nullable: false),
                    Field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Value = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_AssetId_WhenUtc",
                table: "TimelineEntries",
                columns: new[] { "AssetId", "WhenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_SiteId_WhenUtc",
                table: "TimelineEntries",
                columns: new[] { "SiteId", "WhenUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Serial",
                table: "Assets");
        }
    }
}
