using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteAccessSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessSchedule",
                table: "Sites",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // From the Uptick rows already stored (re-imports skip unchanged rows, so wouldn't fill it in)
            migrationBuilder.Sql(@"
UPDATE ""Sites"" SET ""AccessSchedule"" = left(""UptickData""->>'Access Schedule', 20)
WHERE ""UptickData"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessSchedule",
                table: "Sites");
        }
    }
}
