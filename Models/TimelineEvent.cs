using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// One line on a record's Timeline, as in Uptick: a note someone added, a field that changed
// ("Access note was changed to ..."), or an event ("Asset was created."). Uptick's events come from its
// timelineevents endpoint (UptickId set); FyreApp's own are added as people work here.
public class TimelineEvent
{
    public int Id { get; set; }

    // Uptick's id for the event; null for one made in FyreApp
    public long? UptickId { get; set; }

    // The record the event is on, as Uptick names it (its content type's model: "property", "asset", "task", ...)
    // and its Uptick id. Kept for every Uptick event, so records FyreApp doesn't hold yet can be linked later.
    [StringLength(50)]
    public string? UptickObjectType { get; set; }
    public long? UptickObjectId { get; set; }

    // The property or the asset the event is on, when FyreApp holds it
    public int? PropertyId { get; set; }
    public Property? Property { get; set; }
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    public DateTime WhenUtc { get; set; } = DateTime.UtcNow;

    // Who did it: the FyreApp user's name, or Uptick's author ("System Generated" for Uptick's own)
    [StringLength(200)]
    public string? Author { get; set; }

    // A note someone added (shown by "Notes only")
    public bool IsNote { get; set; }

    // Uptick's kind of event: trackedchange, general (notes and others), correspondence, issuance, ...
    [StringLength(50)]
    public string? Key { get; set; }

    // A change made in FyreApp: Field was changed to Value (Value null = cleared)
    [StringLength(100)]
    public string? Field { get; set; }
    public string? Value { get; set; }

    // A note's text, or an event such as "Property was created." (plain text)
    public string? Text { get; set; }

    // An Uptick event's text, its template filled in (TimelineText): safe HTML with only em, p and small
    public string? Html { get; set; }

    // Where the Uptick app was when the event happened (Uptick's "· Map" link)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
