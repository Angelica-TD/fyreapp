using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickRoutineOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Sites",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoutineOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SiteId = table.Column<int>(type: "integer", nullable: false),
                    Routine = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RoutineServiceLevelId = table.Column<int>(type: "integer", nullable: true),
                    MaintenanceScheduleId = table.Column<int>(type: "integer", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ToleranceStart = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ToleranceEnd = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ServiceGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineOccurrences_MaintenanceSchedules_MaintenanceSchedule~",
                        column: x => x.MaintenanceScheduleId,
                        principalTable: "MaintenanceSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoutineOccurrences_RoutineServiceLevels_RoutineServiceLevel~",
                        column: x => x.RoutineServiceLevelId,
                        principalTable: "RoutineServiceLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoutineOccurrences_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutineOccurrences_DueDate_Status",
                table: "RoutineOccurrences",
                columns: new[] { "DueDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutineOccurrences_ExternalId",
                table: "RoutineOccurrences",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineOccurrences_MaintenanceScheduleId",
                table: "RoutineOccurrences",
                column: "MaintenanceScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineOccurrences_RoutineServiceLevelId",
                table: "RoutineOccurrences",
                column: "RoutineServiceLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineOccurrences_SiteId",
                table: "RoutineOccurrences",
                column: "SiteId");

            // Properties already imported: Uptick status from their stored Uptick row
            migrationBuilder.Sql(@"UPDATE ""Sites"" SET ""Status"" = upper(""UptickData""::jsonb ->> 'Status') WHERE ""UptickData"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoutineOccurrences");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Sites");
        }
    }
}
