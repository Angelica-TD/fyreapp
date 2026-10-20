using FyreApp.Models;

namespace FyreApp.ViewModels.Lists;

// Uptick's emails: searched by subject or address, optionally those about one record ("tasks" 46781)
public class CorrespondenceFilter
{
    public string? Search { get; set; }
    public string? Record { get; set; }
    public long? RecordId { get; set; }
    public int Page { get; set; } = 1;

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FilterQuery.Kv("search", Search);
        yield return FilterQuery.Kv("record", Record);
        yield return FilterQuery.Kv("recordId", RecordId?.ToString());
    }
}

public class CorrespondenceListItemVm
{
    public int Id { get; init; }
    public string? Subject { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTime? SentUtc { get; init; }
    public int Attachments { get; init; }
}

public class CorrespondenceListVm
{
    public CorrespondenceFilter Filter { get; set; } = new();

    // The record the list is filtered to, as Uptick names it ("T-46781", "Dpro")
    public RecordLinkVm? RecordLink { get; set; }

    public IReadOnlyList<CorrespondenceListItemVm> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => ListFilters.Pages(Total);
}

// A record an email is about, with its FyreApp page (controller and id) when FyreApp has one
public sealed record RecordLinkVm(string Kind, string Name, string? Controller, int? Id);

// An email's attachment: the report it is (its page), and FyreApp's copy of the file, when FyreApp holds them
public sealed record AttachmentVm(string Name, int? ReportId, int? FileId);

public class CorrespondencePageVm
{
    public Correspondence Email { get; init; } = null!;
    public IReadOnlyList<RecordLinkVm> Records { get; init; } = [];
    public IReadOnlyList<AttachmentVm> Attachments { get; init; } = [];
    public Dispatch? Dispatch { get; init; }
}

// A record's emails (the "Correspondence" panel): the latest few, how many in all, and its dispatches
public class CorrespondencePanelVm
{
    public string Record { get; init; } = string.Empty;
    public long RecordId { get; init; }
    public IReadOnlyList<CorrespondenceListItemVm> Latest { get; init; } = [];
    public int Total { get; init; }
    public IReadOnlyList<DispatchRowVm> Dispatches { get; init; } = [];
}

public sealed record DispatchRowVm(Dispatch Dispatch, int? EmailId, IReadOnlyList<AttachmentVm> Attachments);
