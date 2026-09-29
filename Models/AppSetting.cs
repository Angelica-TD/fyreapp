using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// Simple key/value store for runtime toggles (e.g. DataReset:Enabled)
public class AppSetting
{
    [Key]
    [StringLength(100)]
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
