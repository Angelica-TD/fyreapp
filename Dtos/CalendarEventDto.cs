using System;

namespace FyreApp.Dtos;

public sealed class CalendarEventDto
{
    public string Title { get; set; } = "";
    public string Start { get; set; } = "";   // ISO string
    public string? Url { get; set; }
    public string? ClassName { get; set; }   // e.g. "due", "overdue"
}
