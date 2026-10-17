using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <summary>
    /// TimelineEntry becomes TimelineEvent (Uptick's name), holding Uptick's own timeline events
    /// (docs/uptick-migration.md §5.6, phase 2). A rename, so notes and changes made in FyreApp are kept; the
    /// changes the file imports made up under "Uptick" go, Uptick's real events replacing them.
    /// </summary>
    public partial class MapUptickTimelineEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "TimelineEntries" WHERE "Author" = 'Uptick' AND NOT "IsNote";""");

            migrationBuilder.DropForeignKey(name: "FK_TimelineEntries_Assets_AssetId", table: "TimelineEntries");
            migrationBuilder.DropForeignKey(name: "FK_TimelineEntries_Sites_SiteId", table: "TimelineEntries");
            migrationBuilder.DropPrimaryKey(name: "PK_TimelineEntries", table: "TimelineEntries");

            migrationBuilder.RenameTable(name: "TimelineEntries", newName: "TimelineEvents");
            migrationBuilder.RenameIndex(name: "IX_TimelineEntries_AssetId_WhenUtc", table: "TimelineEvents", newName: "IX_TimelineEvents_AssetId_WhenUtc");
            migrationBuilder.RenameIndex(name: "IX_TimelineEntries_SiteId_WhenUtc", table: "TimelineEvents", newName: "IX_TimelineEvents_SiteId_WhenUtc");
            migrationBuilder.Sql("""ALTER SEQUENCE IF EXISTS "TimelineEntries_Id_seq" RENAME TO "TimelineEvents_Id_seq";""");

            migrationBuilder.AddPrimaryKey(name: "PK_TimelineEvents", table: "TimelineEvents", column: "Id");
            migrationBuilder.AddForeignKey(name: "FK_TimelineEvents_Assets_AssetId", table: "TimelineEvents", column: "AssetId",
                principalTable: "Assets", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_TimelineEvents_Sites_SiteId", table: "TimelineEvents", column: "SiteId",
                principalTable: "Sites", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddColumn<long>(name: "UptickId", table: "TimelineEvents", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "UptickObjectType", table: "TimelineEvents", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.AddColumn<long>(name: "UptickObjectId", table: "TimelineEvents", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Key", table: "TimelineEvents", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Html", table: "TimelineEvents", type: "text", nullable: true);
            migrationBuilder.AddColumn<double>(name: "Latitude", table: "TimelineEvents", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<double>(name: "Longitude", table: "TimelineEvents", type: "double precision", nullable: true);

            migrationBuilder.CreateIndex(name: "IX_TimelineEvents_UptickId", table: "TimelineEvents", column: "UptickId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_TimelineEvents_UptickObjectType_UptickObjectId", table: "TimelineEvents",
                columns: new[] { "UptickObjectType", "UptickObjectId" });

            migrationBuilder.AddColumn<int>(name: "MappedCount", table: "UptickEndpointStates", type: "integer", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MappedCount", table: "UptickEndpointStates");

            // Uptick's events are in the raw copy; only FyreApp's own go back
            migrationBuilder.Sql("""DELETE FROM "TimelineEvents" WHERE "UptickId" IS NOT NULL;""");

            migrationBuilder.DropIndex(name: "IX_TimelineEvents_UptickId", table: "TimelineEvents");
            migrationBuilder.DropIndex(name: "IX_TimelineEvents_UptickObjectType_UptickObjectId", table: "TimelineEvents");
            foreach (var column in new[] { "UptickId", "UptickObjectType", "UptickObjectId", "Key", "Html", "Latitude", "Longitude" })
                migrationBuilder.DropColumn(name: column, table: "TimelineEvents");

            migrationBuilder.DropForeignKey(name: "FK_TimelineEvents_Assets_AssetId", table: "TimelineEvents");
            migrationBuilder.DropForeignKey(name: "FK_TimelineEvents_Sites_SiteId", table: "TimelineEvents");
            migrationBuilder.DropPrimaryKey(name: "PK_TimelineEvents", table: "TimelineEvents");

            migrationBuilder.RenameTable(name: "TimelineEvents", newName: "TimelineEntries");
            migrationBuilder.RenameIndex(name: "IX_TimelineEvents_AssetId_WhenUtc", table: "TimelineEntries", newName: "IX_TimelineEntries_AssetId_WhenUtc");
            migrationBuilder.RenameIndex(name: "IX_TimelineEvents_SiteId_WhenUtc", table: "TimelineEntries", newName: "IX_TimelineEntries_SiteId_WhenUtc");
            migrationBuilder.Sql("""ALTER SEQUENCE IF EXISTS "TimelineEvents_Id_seq" RENAME TO "TimelineEntries_Id_seq";""");

            migrationBuilder.AddPrimaryKey(name: "PK_TimelineEntries", table: "TimelineEntries", column: "Id");
            migrationBuilder.AddForeignKey(name: "FK_TimelineEntries_Assets_AssetId", table: "TimelineEntries", column: "AssetId",
                principalTable: "Assets", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_TimelineEntries_Sites_SiteId", table: "TimelineEntries", column: "SiteId",
                principalTable: "Sites", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        }
    }
}
