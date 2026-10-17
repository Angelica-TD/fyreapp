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
        //             Properties =
        //             {
        //                 new Property
        //                 {
        //                     Name = "New York Property",
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
        //                 new Property
        //                 {
        //                     Name = "Los Angeles Property",
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
        //             Properties =
        //             {
        //                 new Property
        //                 {
        //                     Name = "London Property",
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



        context.SaveChanges();
    }
}
