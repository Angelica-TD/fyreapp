using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    // Uptick users become the source of truth for who's who: logins link to an Uptick user, and tasks are
    // assigned to Uptick users instead of logins. Existing assignments carry over through the login's link.
    public partial class AssignTasksToUptickUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UptickUserId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedToUptickUserId",
                table: "ClientTasks",
                type: "integer",
                nullable: true);

            // Link each login to the one Uptick user with its email (the Users page's "Update from Uptick"
            // links the rest and copies names across)
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" a
                SET "UptickUserId" = m.id
                FROM (
                    SELECT lower(trim("Email")) AS email, min("Id") AS id
                    FROM "UptickUsers"
                    WHERE "Email" IS NOT NULL AND trim("Email") <> ''
                    GROUP BY lower(trim("Email"))
                    HAVING count(*) = 1
                ) m
                WHERE lower(trim(a."Email")) = m.email;
                """);

            // Tasks assigned to a login are now assigned to that login's Uptick user
            migrationBuilder.Sql("""
                UPDATE "ClientTasks" t
                SET "AssignedToUptickUserId" = a."UptickUserId"
                FROM "AspNetUsers" a
                WHERE t."AssignedToUserId" = a."Id" AND a."UptickUserId" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ClientTasks_AspNetUsers_AssignedToUserId",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_AssignedToUserId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "ClientTasks");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_AssignedToUptickUserId",
                table: "ClientTasks",
                column: "AssignedToUptickUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_UptickUserId",
                table: "AspNetUsers",
                column: "UptickUserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_UptickUsers_UptickUserId",
                table: "AspNetUsers",
                column: "UptickUserId",
                principalTable: "UptickUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ClientTasks_UptickUsers_AssignedToUptickUserId",
                table: "ClientTasks",
                column: "AssignedToUptickUserId",
                principalTable: "UptickUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_UptickUsers_UptickUserId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientTasks_UptickUsers_AssignedToUptickUserId",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_AssignedToUptickUserId",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_UptickUserId",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<string>(
                name: "AssignedToUserId",
                table: "ClientTasks",
                type: "text",
                nullable: true);

            // Back to the login, where the Uptick user has one
            migrationBuilder.Sql("""
                UPDATE "ClientTasks" t
                SET "AssignedToUserId" = a."Id"
                FROM "AspNetUsers" a
                WHERE a."UptickUserId" = t."AssignedToUptickUserId";
                """);

            migrationBuilder.DropColumn(
                name: "AssignedToUptickUserId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "UptickUserId",
                table: "AspNetUsers");

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
    }
}
