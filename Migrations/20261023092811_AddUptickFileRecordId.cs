using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddUptickFileRecordId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecordId",
                table: "UptickFiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UptickFiles_RecordType_RecordId",
                table: "UptickFiles",
                columns: new[] { "RecordType", "RecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UptickFiles_RecordType_RecordId",
                table: "UptickFiles");

            migrationBuilder.DropColumn(
                name: "RecordId",
                table: "UptickFiles");
        }
    }
}
