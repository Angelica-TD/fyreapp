namespace FyreApp.ViewModels.Lists;

// What a list page's Edit applies to: the ticked rows, or (AllMatching) every row matching the filters
public record BulkSelection(IReadOnlyCollection<int> Ids, bool AllMatching)
{
    public static BulkSelection From(List<int>? ids, bool allMatching) => new(ids ?? new List<int>(), allMatching);
}

// Fields of the "Generate tasks" Edit action on Properties, Assets and Remarks
public class BulkTaskInput
{
    public string? Title { get; set; }
    public string? Category { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Description { get; set; }
}

// Uptick's task categories, offered by the Generate tasks forms
public static class TaskCategories
{
    public static readonly string[] All = { "I&T", "Repair", "Callout", "Billing" };
}
