using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOfferings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceOfferings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOfferings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOfferingIntervals",
                columns: table => new
                {
                    IntervalsId = table.Column<int>(type: "integer", nullable: false),
                    ServiceOfferingsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOfferingIntervals", x => new { x.IntervalsId, x.ServiceOfferingsId });
                    table.ForeignKey(
                        name: "FK_ServiceOfferingIntervals_MaintenanceIntervals_IntervalsId",
                        column: x => x.IntervalsId,
                        principalTable: "MaintenanceIntervals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceOfferingIntervals_ServiceOfferings_ServiceOfferingsId",
                        column: x => x.ServiceOfferingsId,
                        principalTable: "ServiceOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOfferingIntervals_ServiceOfferingsId",
                table: "ServiceOfferingIntervals",
                column: "ServiceOfferingsId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOfferings_Name",
                table: "ServiceOfferings",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceOfferingIntervals");

            migrationBuilder.DropTable(
                name: "ServiceOfferings");
        }
    }
}
