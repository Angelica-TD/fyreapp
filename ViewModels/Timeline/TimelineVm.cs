using FyreApp.Models;

namespace FyreApp.ViewModels.Timeline;

// The Timeline card on a property or asset page, as in Uptick: newest first, "Notes only" and "Add note"
public class TimelineVm
{
    public IReadOnlyList<TimelineEvent> Entries { get; init; } = [];
    public bool NotesOnly { get; init; }

    // Where "Add note" posts and the "Notes only" toggle links (the page with/without notes=true)
    public required string AddNoteUrl { get; init; }
    public required string ToggleNotesUrl { get; init; }
}
