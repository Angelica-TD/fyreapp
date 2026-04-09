using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class AssetCatalogue
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
}
