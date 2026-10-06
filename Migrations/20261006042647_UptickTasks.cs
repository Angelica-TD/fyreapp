using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "ClientTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ref",
                table: "ClientTasks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "ClientTasks",
                type: "json",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientTaskCoveredSchedules",
                columns: table => new
                {
                    CoveredSchedulesId = table.Column<int>(type: "integer", nullable: false),
                    CoveringTasksId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTaskCoveredSchedules", x => new { x.CoveredSchedulesId, x.CoveringTasksId });
                    table.ForeignKey(
                        name: "FK_ClientTaskCoveredSchedules_ClientTasks_CoveringTasksId",
                        column: x => x.CoveringTasksId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientTaskCoveredSchedules_MaintenanceSchedules_CoveredSche~",
                        column: x => x.CoveredSchedulesId,
                        principalTable: "MaintenanceSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_ExternalId",
                table: "ClientTasks",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTaskCoveredSchedules_CoveringTasksId",
                table: "ClientTaskCoveredSchedules",
                column: "CoveringTasksId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientTaskCoveredSchedules");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_ExternalId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "Ref",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "ClientTasks");
        }
    }
}
