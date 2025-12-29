using FyreApp.Models;

namespace FyreApp.Data;

public static class DbInitialiser
{
    public static void Seed(AppDbContext context)
    {
        // Prevent duplicate seeding
        if (!context.Clients.Any())
        {
            var clients = new List<Client>
            {
                new Client
                {
                    Name = "Acme Corp",
                    Sites =
                    {
                        new Site
                        {
                            Name = "New York Site",
                            Assets =
                            {
                                new Asset
                                {
                                    Name = "Generator A",
                                    AssetTypes =
                                    {
                                        new AssetType { Name = "Electrical" },
                                        new AssetType { Name = "Critical" }
                                    }
                                },
                                new Asset
                                {
                                    Name = "UPS A",
                                    AssetTypes =
                                    {
                                        new AssetType { Name = "Backup" }
                                    }
                                }
                            }
                        },
                        new Site
                        {
                            Name = "Los Angeles Site",
                            Assets =
                            {
                                new Asset
                                {
                                    Name = "HVAC Unit",
                                    AssetTypes =
                                    {
                                        new AssetType { Name = "Mechanical" }
                                    }
                                }
                            }
                        }
                    }
                },
                new Client
                {
                    Name = "Globex Ltd",
                    Sites =
                    {
                        new Site
                        {
                            Name = "London Site",
                            Assets =
                            {
                                new Asset
                                {
                                    Name = "Server Rack",
                                    AssetTypes =
                                    {
                                        new AssetType { Name = "IT" },
                                        new AssetType { Name = "Critical" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            context.Clients.AddRange(clients);
            // context.SaveChanges();
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
