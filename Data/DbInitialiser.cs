using FyreApp.Models;

namespace FyreApp.Data;

public static class DbInitialiser
{
    public static void Seed(AppDbContext context)
    {
        // Prevent duplicate seeding
        // if (!context.Clients.Any())
        // {
        //     var clients = new List<Client>
        //     {
        //         new Client
        //         {
        //             Name = "Acme Corp",
        //             Sites =
        //             {
        //                 new Site
        //                 {
        //                     Name = "New York Site",
        //                     Assets =
        //                     {
        //                         new Asset
        //                         {
        //                             Name = "Generator A",
        //                             AssetTypes =
        //                             {
        //                                 new AssetType { Name = "Electrical" },
        //                                 new AssetType { Name = "Critical" }
        //                             }
        //                         },
        //                         new Asset
        //                         {
        //                             Name = "UPS A",
        //                             AssetTypes =
        //                             {
        //                                 new AssetType { Name = "Backup" }
        //                             }
        //                         }
        //                     }
        //                 },
        //                 new Site
        //                 {
        //                     Name = "Los Angeles Site",
        //                     Assets =
        //                     {
        //                         new Asset
        //                         {
        //                             Name = "HVAC Unit",
        //                             AssetTypes =
        //                             {
        //                                 new AssetType { Name = "Mechanical" }
        //                             }
        //                         }
        //                     }
        //                 }
        //             }
        //         },
        //         new Client
        //         {
        //             Name = "Globex Ltd",
        //             Sites =
        //             {
        //                 new Site
        //                 {
        //                     Name = "London Site",
        //                     Assets =
        //                     {
        //                         new Asset
        //                         {
        //                             Name = "Server Rack",
        //                             AssetTypes =
        //                             {
        //                                 new AssetType { Name = "IT" },
        //                                 new AssetType { Name = "Critical" }
        //                             }
        //                         }
        //                     }
        //                 }
        //             }
        //         }
        //     };

        //     context.Clients.AddRange(clients);
        //     // context.SaveChanges();
        // }



        if (!context.AssetCatalogue.Any())
        {
            context.AssetCatalogue.AddRange(
                // Extinguishers
                new AssetCatalogue { Name = "CO2 Fire Extinguisher" },
                new AssetCatalogue { Name = "Dry Powder Fire Extinguisher" },
                new AssetCatalogue { Name = "Wet Chemical Fire Extinguisher" },
                new AssetCatalogue { Name = "Water Fire Extinguisher" },
                new AssetCatalogue { Name = "Foam Fire Extinguisher" },
                new AssetCatalogue { Name = "Vaporising Liquid Fire Extinguisher" },
                // Detection
                new AssetCatalogue { Name = "Smoke Detector" },
                new AssetCatalogue { Name = "Heat Detector" },
                new AssetCatalogue { Name = "Flame Detector" },
                new AssetCatalogue { Name = "Carbon Monoxide Detector" },
                new AssetCatalogue { Name = "Multi-Sensor Detector" },
                new AssetCatalogue { Name = "Aspirating Smoke Detection (ASD)" },
                new AssetCatalogue { Name = "VESDA System" },
                new AssetCatalogue { Name = "Beam Detector" },
                // Suppression
                new AssetCatalogue { Name = "Sprinkler System" },
                new AssetCatalogue { Name = "Wet Pipe Sprinkler System" },
                new AssetCatalogue { Name = "Dry Pipe Sprinkler System" },
                new AssetCatalogue { Name = "Pre-Action Sprinkler System" },
                new AssetCatalogue { Name = "Deluge System" },
                new AssetCatalogue { Name = "Kitchen Suppression System" },
                new AssetCatalogue { Name = "Clean Agent Suppression System" },
                new AssetCatalogue { Name = "Foam Suppression System" },
                new AssetCatalogue { Name = "Gaseous Suppression System" },
                // Hose & Hydrant
                new AssetCatalogue { Name = "Fire Hose Reel" },
                new AssetCatalogue { Name = "Fire Hydrant" },
                new AssetCatalogue { Name = "Fire Hydrant Booster" },
                new AssetCatalogue { Name = "Landing Valve" },
                // Panels & Control
                new AssetCatalogue { Name = "Fire Indicator Panel (FIP)" },
                new AssetCatalogue { Name = "Fire Alarm Panel" },
                new AssetCatalogue { Name = "Alarm Monitoring System" },
                new AssetCatalogue { Name = "Sprinkler Control Valve" },
                // Emergency Lighting
                new AssetCatalogue { Name = "Emergency Exit Light" },
                new AssetCatalogue { Name = "Emergency Light" },
                new AssetCatalogue { Name = "Exit Sign" },
                // Warning & Communication
                new AssetCatalogue { Name = "Emergency Warning & Intercom System (EWIS)" },
                new AssetCatalogue { Name = "Occupant Warning System" },
                new AssetCatalogue { Name = "Public Address System" },
                // Passive & Other
                new AssetCatalogue { Name = "Fire Blanket" },
                new AssetCatalogue { Name = "Fire Door" },
                new AssetCatalogue { Name = "Fire Damper" },
                new AssetCatalogue { Name = "Fire Curtain" },
                new AssetCatalogue { Name = "Fire Shutter" },
                new AssetCatalogue { Name = "Evacuation Diagram" }
            );
        }

        if (!context.MaintenanceIntervals.Any())
        {
            context.MaintenanceIntervals.AddRange(
                new MaintenanceInterval
                {
                    Name = "Monthly",
                    Months = 1
                },
                new MaintenanceInterval
                {
                    Name = "6-Monthly",
                    Months = 6
                },
                new MaintenanceInterval
                {
                    Name = "Yearly",
                    Months = 12
                },
                new MaintenanceInterval
                {
                    Name = "Five-Yearly",
                    Months = 60
                }
            );

        }

        context.SaveChanges();
    }
}
