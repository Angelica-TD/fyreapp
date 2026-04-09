using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class SeedMaintenanceIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MaintenanceIntervals",
                columns: new[] { "Name", "Months" },
                values: new object[,]
                {
                    { "Monthly",     1  },
                    { "6-Monthly",   6  },
                    { "Yearly",      12 },
                    { "Five-Yearly", 60 },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "MaintenanceIntervals",
                keyColumn: "Name",
                keyValues: new object[] { "Monthly", "6-Monthly", "Yearly", "Five-Yearly" });
        }
    }
}
