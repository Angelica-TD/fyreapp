using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureAssignedToForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientTasks_AspNetUsers_AssignedToId",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_AssignedToId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "AssignedToId",
                table: "ClientTasks");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_AssignedToUserId",
                table: "ClientTasks",
                column: "AssignedToUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientTasks_AspNetUsers_AssignedToUserId",
                table: "ClientTasks",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientTasks_AspNetUsers_AssignedToUserId",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_AssignedToUserId",
                table: "ClientTasks");

            migrationBuilder.AddColumn<string>(
                name: "AssignedToId",
                table: "ClientTasks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_AssignedToId",
                table: "ClientTasks",
                column: "AssignedToId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientTasks_AspNetUsers_AssignedToId",
                table: "ClientTasks",
                column: "AssignedToId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
