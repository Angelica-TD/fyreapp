using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddUptickMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UptickEndpointStates",
                columns: table => new
                {
                    Resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IncrementalVerified = table.Column<bool>(type: "boolean", nullable: false),
                    IncrementalCheckedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CursorUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFullRunUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRefreshUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UptickCount = table.Column<int>(type: "integer", nullable: true),
                    CopiedCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    Schema = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptickEndpointStates", x => x.Resource);
                });

            migrationBuilder.CreateTable(
                name: "UptickMigrationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Requests = table.Column<int>(type: "integer", nullable: false),
                    Throttled = table.Column<int>(type: "integer", nullable: false),
                    Report = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptickMigrationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UptickRecords",
                columns: table => new
                {
                    Resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UptickId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    Relationships = table.Column<string>(type: "jsonb", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FetchedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MappedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptickRecords", x => new { x.Resource, x.UptickId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_UptickMigrationRuns_StartedUtc",
                table: "UptickMigrationRuns",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UptickRecords_Relationships",
                table: "UptickRecords",
                column: "Relationships")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_UptickRecords_Resource_MappedUtc",
                table: "UptickRecords",
                columns: new[] { "Resource", "MappedUtc" },
                filter: "\"MappedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UptickRecords_Resource_UpdatedUtc",
                table: "UptickRecords",
                columns: new[] { "Resource", "UpdatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UptickEndpointStates");

            migrationBuilder.DropTable(
                name: "UptickMigrationRuns");

            migrationBuilder.DropTable(
                name: "UptickRecords");
        }
    }
}
