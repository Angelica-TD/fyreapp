using System;

namespace FyreApp.Models;

public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Navigation
    public ICollection<Site> Sites { get; set; } = new List<Site>();
}
