using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RemarkTypeId",
                table: "Defects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "AssetTypes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "AssetTypes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AssetTypes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "AssetTypes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectionCriteria",
                table: "AssetTypes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SequenceGroup",
                table: "AssetTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "AssetTypes",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssetTypeVariantId",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetTypeVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssetTypeId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultReplacementProduct = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTypeVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetTypeVariants_AssetTypes_AssetTypeId",
                        column: x => x.AssetTypeId,
                        principalTable: "AssetTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemarkTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AssetTypeId = table.Column<int>(type: "integer", nullable: true),
                    AssetTypeTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SeverityLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Resolution = table.Column<string>(type: "text", nullable: true),
                    OwnerResponsible = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemarkTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemarkTypes_AssetTypes_AssetTypeId",
                        column: x => x.AssetTypeId,
                        principalTable: "AssetTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RoutineServiceTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Standard = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StandardReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StandardNotes = table.Column<string>(type: "text", nullable: true),
                    DefaultPerformanceStandard = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ServiceGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Custom = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineServiceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoutineServiceLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RoutineServiceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    OffsetMonths = table.Column<int>(type: "integer", nullable: true),
                    ToleranceInterval = table.Column<int>(type: "integer", nullable: true),
                    ToleranceUnit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DefaultEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AssetBased = table.Column<bool>(type: "boolean", nullable: false),
                    SupersedesLowerRank = table.Column<bool>(type: "boolean", nullable: false),
                    ServiceRank = table.Column<int>(type: "integer", nullable: true),
                    Custom = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineServiceLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineServiceLevels_RoutineServiceTypes_RoutineServiceType~",
                        column: x => x.RoutineServiceTypeId,
                        principalTable: "RoutineServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceScheduleRoutineLevels",
                columns: table => new
                {
                    RoutineLevelsId = table.Column<int>(type: "integer", nullable: false),
                    SchedulesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceScheduleRoutineLevels", x => new { x.RoutineLevelsId, x.SchedulesId });
                    table.ForeignKey(
                        name: "FK_MaintenanceScheduleRoutineLevels_MaintenanceSchedules_Sched~",
                        column: x => x.SchedulesId,
                        principalTable: "MaintenanceSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceScheduleRoutineLevels_RoutineServiceLevels_Routi~",
                        column: x => x.RoutineLevelsId,
                        principalTable: "RoutineServiceLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Defects_RemarkTypeId",
                table: "Defects",
                column: "RemarkTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypes_ExternalId",
                table: "AssetTypes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_AssetTypeVariantId",
                table: "Assets",
                column: "AssetTypeVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypeVariants_AssetTypeId_Name",
                table: "AssetTypeVariants",
                columns: new[] { "AssetTypeId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypeVariants_ExternalId",
                table: "AssetTypeVariants",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceScheduleRoutineLevels_SchedulesId",
                table: "MaintenanceScheduleRoutineLevels",
                column: "SchedulesId");

            migrationBuilder.CreateIndex(
                name: "IX_RemarkTypes_AssetTypeId",
                table: "RemarkTypes",
                column: "AssetTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RemarkTypes_ExternalId",
                table: "RemarkTypes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceLevels_ExternalId",
                table: "RoutineServiceLevels",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceLevels_RoutineServiceTypeId",
                table: "RoutineServiceLevels",
                column: "RoutineServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceTypes_ExternalId",
                table: "RoutineServiceTypes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceTypes_Name",
                table: "RoutineServiceTypes",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_AssetTypeVariants_AssetTypeVariantId",
                table: "Assets",
                column: "AssetTypeVariantId",
                principalTable: "AssetTypeVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_RemarkTypes_RemarkTypeId",
                table: "Defects",
                column: "RemarkTypeId",
                principalTable: "RemarkTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_AssetTypeVariants_AssetTypeVariantId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_RemarkTypes_RemarkTypeId",
                table: "Defects");

            migrationBuilder.DropTable(
                name: "AssetTypeVariants");

            migrationBuilder.DropTable(
                name: "MaintenanceScheduleRoutineLevels");

            migrationBuilder.DropTable(
                name: "RemarkTypes");

            migrationBuilder.DropTable(
                name: "RoutineServiceLevels");

            migrationBuilder.DropTable(
                name: "RoutineServiceTypes");

            migrationBuilder.DropIndex(
                name: "IX_Defects_RemarkTypeId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_AssetTypes_ExternalId",
                table: "AssetTypes");

            migrationBuilder.DropIndex(
                name: "IX_Assets_AssetTypeVariantId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "RemarkTypeId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "InspectionCriteria",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "SequenceGroup",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "AssetTypeVariantId",
                table: "Assets");
        }
    }
}
