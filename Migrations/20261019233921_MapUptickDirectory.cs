using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Suppliers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "SecurityGroups",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Contractors",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Accreditations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "Accreditations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UptickUserId",
                table: "Accreditations",
                type: "integer",
                nullable: true);

            // Records the exports made carry Uptick's id in ExternalId, so the mappers update them
            foreach (var table in new[] { "SecurityGroups", "Contractors", "Suppliers", "Accreditations" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_UptickId",
                table: "Suppliers",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityGroups_UptickId",
                table: "SecurityGroups",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_UptickId",
                table: "Contractors",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accreditations_UptickId",
                table: "Accreditations",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accreditations_UptickUserId",
                table: "Accreditations",
                column: "UptickUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Accreditations_UptickUsers_UptickUserId",
                table: "Accreditations",
                column: "UptickUserId",
                principalTable: "UptickUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accreditations_UptickUsers_UptickUserId",
                table: "Accreditations");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_UptickId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_SecurityGroups_UptickId",
                table: "SecurityGroups");

            migrationBuilder.DropIndex(
                name: "IX_Contractors_UptickId",
                table: "Contractors");

            migrationBuilder.DropIndex(
                name: "IX_Accreditations_UptickId",
                table: "Accreditations");

            migrationBuilder.DropIndex(
                name: "IX_Accreditations_UptickUserId",
                table: "Accreditations");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "SecurityGroups");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Accreditations");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "Accreditations");

            migrationBuilder.DropColumn(
                name: "UptickUserId",
                table: "Accreditations");
        }
    }
}
