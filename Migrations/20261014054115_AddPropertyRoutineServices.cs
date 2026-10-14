using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyRoutineServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PropertyRoutineServiceId",
                table: "RoutineOccurrences",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PropertyRoutineServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SiteId = table.Column<int>(type: "integer", nullable: false),
                    RoutineServiceTypeId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    LastAnnualDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Subcontractor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyRoutineServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyRoutineServices_RoutineServiceTypes_RoutineServiceT~",
                        column: x => x.RoutineServiceTypeId,
                        principalTable: "RoutineServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropertyRoutineServices_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyRoutineServiceLevels",
                columns: table => new
                {
                    LevelsId = table.Column<int>(type: "integer", nullable: false),
                    PropertyRoutineServiceId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyRoutineServiceLevels", x => new { x.LevelsId, x.PropertyRoutineServiceId });
                    table.ForeignKey(
                        name: "FK_PropertyRoutineServiceLevels_PropertyRoutineServices_Proper~",
                        column: x => x.PropertyRoutineServiceId,
                        principalTable: "PropertyRoutineServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PropertyRoutineServiceLevels_RoutineServiceLevels_LevelsId",
                        column: x => x.LevelsId,
                        principalTable: "RoutineServiceLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutineOccurrences_PropertyRoutineServiceId",
                table: "RoutineOccurrences",
                column: "PropertyRoutineServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyRoutineServiceLevels_PropertyRoutineServiceId",
                table: "PropertyRoutineServiceLevels",
                column: "PropertyRoutineServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyRoutineServices_RoutineServiceTypeId",
                table: "PropertyRoutineServices",
                column: "RoutineServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyRoutineServices_SiteId",
                table: "PropertyRoutineServices",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoutineOccurrences_PropertyRoutineServices_PropertyRoutineS~",
                table: "RoutineOccurrences",
                column: "PropertyRoutineServiceId",
                principalTable: "PropertyRoutineServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoutineOccurrences_PropertyRoutineServices_PropertyRoutineS~",
                table: "RoutineOccurrences");

            migrationBuilder.DropTable(
                name: "PropertyRoutineServiceLevels");

            migrationBuilder.DropTable(
                name: "PropertyRoutineServices");

            migrationBuilder.DropIndex(
                name: "IX_RoutineOccurrences_PropertyRoutineServiceId",
                table: "RoutineOccurrences");

            migrationBuilder.DropColumn(
                name: "PropertyRoutineServiceId",
                table: "RoutineOccurrences");
        }
    }
}
