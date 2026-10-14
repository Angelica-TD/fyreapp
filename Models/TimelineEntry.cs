using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// One line on a property's or asset's Timeline, as in Uptick: a note someone added, a field that changed
// ("Access note was changed to ..."), or an event ("Asset was created.").
public class TimelineEntry
{
    public int Id { get; set; }

    // Exactly one of these: the property or the asset the entry belongs to
    public int? SiteId { get; set; }
    public Site? Site { get; set; }
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    public DateTime WhenUtc { get; set; } = DateTime.UtcNow;

    // Who did it: a FyreApp user's name, or "Uptick" for changes that came in from an Uptick import
    [StringLength(200)]
    public string? Author { get; set; }

    // Added with "Add note" (shown by "Notes only")
    public bool IsNote { get; set; }

    // A change: Field was changed to Value (Value null = cleared)
    [StringLength(100)]
    public string? Field { get; set; }
    public string? Value { get; set; }

    // A note's text, or an event such as "Property was created."
    public string? Text { get; set; }
}
