using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskUptickTechnician : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UptickTechnician",
                table: "ClientTasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // From the Uptick rows already stored (re-imports skip unchanged rows, so wouldn't fill it in)
            migrationBuilder.Sql(@"
UPDATE ""ClientTasks"" SET ""UptickTechnician"" = left(nullif(btrim(""UptickData""->>'Assigned Technician'), ''), 200)
WHERE ""UptickData"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UptickTechnician",
                table: "ClientTasks");
        }
    }
}
