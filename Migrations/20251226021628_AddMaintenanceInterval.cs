using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IntervalDays",
                table: "MaintenanceSchedules",
                newName: "MaintenanceIntervalId");

            migrationBuilder.CreateTable(
                name: "MaintenanceIntervals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceIntervals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSchedules_MaintenanceIntervalId",
                table: "MaintenanceSchedules",
                column: "MaintenanceIntervalId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceSchedules_MaintenanceIntervals_MaintenanceInterv~",
                table: "MaintenanceSchedules",
                column: "MaintenanceIntervalId",
                principalTable: "MaintenanceIntervals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceSchedules_MaintenanceIntervals_MaintenanceInterv~",
                table: "MaintenanceSchedules");

            migrationBuilder.DropTable(
                name: "MaintenanceIntervals");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceSchedules_MaintenanceIntervalId",
                table: "MaintenanceSchedules");

            migrationBuilder.RenameColumn(
                name: "MaintenanceIntervalId",
                table: "MaintenanceSchedules",
                newName: "IntervalDays");
        }
    }
}
