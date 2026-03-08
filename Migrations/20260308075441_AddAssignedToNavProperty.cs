using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedToNavProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
