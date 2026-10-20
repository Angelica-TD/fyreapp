using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AssetTypeRoutineLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetTypeApplicableRoutines",
                columns: table => new
                {
                    ApplicableRoutineTypesId = table.Column<int>(type: "integer", nullable: false),
                    AssetTypeId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTypeApplicableRoutines", x => new { x.ApplicableRoutineTypesId, x.AssetTypeId });
                    table.ForeignKey(
                        name: "FK_AssetTypeApplicableRoutines_AssetTypes_AssetTypeId",
                        column: x => x.AssetTypeId,
                        principalTable: "AssetTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetTypeApplicableRoutines_RoutineServiceTypes_ApplicableR~",
                        column: x => x.ApplicableRoutineTypesId,
                        principalTable: "RoutineServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetTypeExcludedRoutineLevels",
                columns: table => new
                {
                    AssetTypeId = table.Column<int>(type: "integer", nullable: false),
                    ExcludedRoutineLevelsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTypeExcludedRoutineLevels", x => new { x.AssetTypeId, x.ExcludedRoutineLevelsId });
                    table.ForeignKey(
                        name: "FK_AssetTypeExcludedRoutineLevels_AssetTypes_AssetTypeId",
                        column: x => x.AssetTypeId,
                        principalTable: "AssetTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetTypeExcludedRoutineLevels_RoutineServiceLevels_Exclude~",
                        column: x => x.ExcludedRoutineLevelsId,
                        principalTable: "RoutineServiceLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypeApplicableRoutines_AssetTypeId",
                table: "AssetTypeApplicableRoutines",
                column: "AssetTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypeExcludedRoutineLevels_ExcludedRoutineLevelsId",
                table: "AssetTypeExcludedRoutineLevels",
                column: "ExcludedRoutineLevelsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetTypeApplicableRoutines");

            migrationBuilder.DropTable(
                name: "AssetTypeExcludedRoutineLevels");
        }
    }
}
