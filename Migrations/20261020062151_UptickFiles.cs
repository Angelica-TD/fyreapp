using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UptickFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    Etag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceUpdatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Md5 = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DownloadedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    CheckedUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    NoFile = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Failures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptickFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UptickFiles_Source_SourceId",
                table: "UptickFiles",
                columns: new[] { "Source", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UptickFiles");
        }
    }
}
