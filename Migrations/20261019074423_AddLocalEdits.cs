using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalEdits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Entity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<int>(type: "integer", nullable: false),
                    Field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FyreAppValue = table.Column<string>(type: "text", nullable: true),
                    UptickValue = table.Column<string>(type: "text", nullable: true),
                    EditedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EditedUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ClosedReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalEdits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalEdits_Entity_RecordId",
                table: "LocalEdits",
                columns: new[] { "Entity", "RecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalEdits");
        }
    }
}
