using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteActiveAndScheduleTaskLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Sites",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MaintenanceScheduleId",
                table: "ClientTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_MaintenanceScheduleId",
                table: "ClientTasks",
                column: "MaintenanceScheduleId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientTasks_MaintenanceSchedules_MaintenanceScheduleId",
                table: "ClientTasks",
                column: "MaintenanceScheduleId",
                principalTable: "MaintenanceSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientTasks_MaintenanceSchedules_MaintenanceScheduleId",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_MaintenanceScheduleId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "MaintenanceScheduleId",
                table: "ClientTasks");
        }
    }
}
