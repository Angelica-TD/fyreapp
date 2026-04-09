using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class SeedAssetCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AssetCatalogue",
                columns: new[] { "Name" },
                values: new object[,]
                {
                    // Extinguishers
                    { "CO2 Fire Extinguisher" },
                    { "Dry Powder Fire Extinguisher" },
                    { "Wet Chemical Fire Extinguisher" },
                    { "Water Fire Extinguisher" },
                    { "Foam Fire Extinguisher" },
                    { "Vaporising Liquid Fire Extinguisher" },
                    // Detection
                    { "Smoke Detector" },
                    { "Heat Detector" },
                    { "Flame Detector" },
                    { "Carbon Monoxide Detector" },
                    { "Multi-Sensor Detector" },
                    { "Aspirating Smoke Detection (ASD)" },
                    { "VESDA System" },
                    { "Beam Detector" },
                    // Suppression
                    { "Sprinkler System" },
                    { "Wet Pipe Sprinkler System" },
                    { "Dry Pipe Sprinkler System" },
                    { "Pre-Action Sprinkler System" },
                    { "Deluge System" },
                    { "Kitchen Suppression System" },
                    { "Clean Agent Suppression System" },
                    { "Foam Suppression System" },
                    { "Gaseous Suppression System" },
                    // Hose & Hydrant
                    { "Fire Hose Reel" },
                    { "Fire Hydrant" },
                    { "Fire Hydrant Booster" },
                    { "Landing Valve" },
                    // Panels & Control
                    { "Fire Indicator Panel (FIP)" },
                    { "Fire Alarm Panel" },
                    { "Alarm Monitoring System" },
                    { "Sprinkler Control Valve" },
                    // Emergency Lighting
                    { "Emergency Exit Light" },
                    { "Emergency Light" },
                    { "Exit Sign" },
                    // Warning & Communication
                    { "Emergency Warning & Intercom System (EWIS)" },
                    { "Occupant Warning System" },
                    { "Public Address System" },
                    // Passive & Other
                    { "Fire Blanket" },
                    { "Fire Door" },
                    { "Fire Damper" },
                    { "Fire Curtain" },
                    { "Fire Shutter" },
                    { "Evacuation Diagram" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AssetCatalogue",
                keyColumn: "Name",
                keyValues: new object[]
                {
                    "CO2 Fire Extinguisher",
                    "Dry Powder Fire Extinguisher",
                    "Wet Chemical Fire Extinguisher",
                    "Water Fire Extinguisher",
                    "Foam Fire Extinguisher",
                    "Vaporising Liquid Fire Extinguisher",
                    "Smoke Detector",
                    "Heat Detector",
                    "Flame Detector",
                    "Carbon Monoxide Detector",
                    "Multi-Sensor Detector",
                    "Aspirating Smoke Detection (ASD)",
                    "VESDA System",
                    "Beam Detector",
                    "Sprinkler System",
                    "Wet Pipe Sprinkler System",
                    "Dry Pipe Sprinkler System",
                    "Pre-Action Sprinkler System",
                    "Deluge System",
                    "Kitchen Suppression System",
                    "Clean Agent Suppression System",
                    "Foam Suppression System",
                    "Gaseous Suppression System",
                    "Fire Hose Reel",
                    "Fire Hydrant",
                    "Fire Hydrant Booster",
                    "Landing Valve",
                    "Fire Indicator Panel (FIP)",
                    "Fire Alarm Panel",
                    "Alarm Monitoring System",
                    "Sprinkler Control Valve",
                    "Emergency Exit Light",
                    "Emergency Light",
                    "Exit Sign",
                    "Emergency Warning & Intercom System (EWIS)",
                    "Occupant Warning System",
                    "Public Address System",
                    "Fire Blanket",
                    "Fire Door",
                    "Fire Damper",
                    "Fire Curtain",
                    "Fire Shutter",
                    "Evacuation Diagram",
                });
        }
    }
}
