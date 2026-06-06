using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AS1851Section = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GeneratesComplianceReport = table.Column<bool>(type: "boolean", nullable: false),
                    ComplianceFormReference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DefaultIntervalId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceTypes_MaintenanceIntervals_DefaultIntervalId",
                        column: x => x.DefaultIntervalId,
                        principalTable: "MaintenanceIntervals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_DefaultIntervalId",
                table: "ServiceTypes",
                column: "DefaultIntervalId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_Name",
                table: "ServiceTypes",
                column: "Name",
                unique: true);

            // Seed service types
            migrationBuilder.InsertData(
                table: "ServiceTypes",
                columns: new[] { "Name", "AS1851Section", "GeneratesComplianceReport", "ComplianceFormReference" },
                values: new object[,]
                {
                    { "Routine Inspection",             "Section 4", false, null },
                    { "Unassisted Flow Test",            "Section 4", true,  "Form 72" },
                    { "Hydrostatic Pressure Test",       "Section 4", true,  "Form 72" },
                    { "Assisted (Booster) Flow Test",    "Section 4", true,  "Form 72" },
                    { "Valve & Component Overhaul",      "Section 4", false, null }
                });

            // Set DefaultIntervalId by matching on interval Months value
            migrationBuilder.Sql(@"
                UPDATE ""ServiceTypes""
                SET ""DefaultIntervalId"" = (
                    SELECT ""Id"" FROM ""MaintenanceIntervals"" WHERE ""Months"" = 6 LIMIT 1
                )
                WHERE ""Name"" = 'Routine Inspection';

                UPDATE ""ServiceTypes""
                SET ""DefaultIntervalId"" = (
                    SELECT ""Id"" FROM ""MaintenanceIntervals"" WHERE ""Months"" = 12 LIMIT 1
                )
                WHERE ""Name"" = 'Unassisted Flow Test';

                UPDATE ""ServiceTypes""
                SET ""DefaultIntervalId"" = (
                    SELECT ""Id"" FROM ""MaintenanceIntervals"" WHERE ""Months"" = 60 LIMIT 1
                )
                WHERE ""Name"" IN ('Hydrostatic Pressure Test', 'Assisted (Booster) Flow Test', 'Valve & Component Overhaul');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ServiceTypes");
        }
    }
}
