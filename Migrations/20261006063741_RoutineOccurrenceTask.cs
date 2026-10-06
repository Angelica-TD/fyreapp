using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class RoutineOccurrenceTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClientTaskId",
                table: "RoutineOccurrences",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineOccurrences_ClientTaskId",
                table: "RoutineOccurrences",
                column: "ClientTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoutineOccurrences_ClientTasks_ClientTaskId",
                table: "RoutineOccurrences",
                column: "ClientTaskId",
                principalTable: "ClientTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoutineOccurrences_ClientTasks_ClientTaskId",
                table: "RoutineOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_RoutineOccurrences_ClientTaskId",
                table: "RoutineOccurrences");

            migrationBuilder.DropColumn(
                name: "ClientTaskId",
                table: "RoutineOccurrences");
        }
    }
}
