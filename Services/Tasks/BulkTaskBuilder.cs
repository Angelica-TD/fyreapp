using FyreApp.Models;
using FyreApp.ViewModels.Lists;

namespace FyreApp.Services.Tasks;

// Builds one task per property for the list pages' "Generate tasks" (Properties, Assets, Remarks).
// The task lists what it was made from (e.g. the selected assets or defects at that property).
public static class BulkTaskBuilder
{
    public static List<ClientTask> PerProperty<T>(
        IEnumerable<T> items, Func<T, Site> siteOf, Func<T, string?> lineOf,
        BulkTaskInput input, string defaultTitle, string? createdByUserId)
    {
        var title = string.IsNullOrWhiteSpace(input.Title) ? defaultTitle : input.Title.Trim();

        return items
            .GroupBy(siteOf)
            .OrderBy(g => g.Key.Id)
            .Select(g =>
            {
                var lines = g.Select(lineOf).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();
                var description = string.Join("\n\n", new[]
                {
                    string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
                    lines.Count == 0 ? null : "Scope of works:\n" + string.Join("\n", lines.Select(l => $"- {l}"))
                }.Where(s => s != null));

                return new ClientTask
                {
                    ClientId = g.Key.ClientId,
                    SiteId = g.Key.Id,
                    Title = Truncate(title, 120),
                    Description = description.Length == 0 ? null : Truncate(description, 4000),
                    Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category,
                    Status = ClientTaskStatus.Open,
                    Priority = ClientTaskPriority.Normal,
                    DueDateUtc = input.DueDate is DateTime due ? DateTime.SpecifyKind(due.Date, DateTimeKind.Utc) : null,
                    CreatedUtc = DateTime.UtcNow,
                    CreatedByUserId = createdByUserId
                };
            })
            .ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
