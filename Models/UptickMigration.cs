using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// One record of one Uptick API endpoint, exactly as the API returned it (docs/uptick-migration.md §3.2).
// Every endpoint lands here first; FyreApp's own tables are mapped from these rows.
public class UptickRecord
{
    // The endpoint, e.g. "assets" or "intelligencereports/profitability_by_task"
    [StringLength(100)] public string Resource { get; set; } = string.Empty;

    // The record's JSON:API id (numeric for almost every endpoint, but not guaranteed)
    [StringLength(64)] public string UptickId { get; set; } = string.Empty;

    // JSON:API type, e.g. "Asset"
    [StringLength(100)] public string? Type { get; set; }

    public string Attributes { get; set; } = "{}";
    public string Relationships { get; set; } = "{}";

    // Of the attributes and relationships as received, to tell whether a record changed without comparing JSON
    [StringLength(64)] public string Hash { get; set; } = string.Empty;

    // The record's own "created" / "updated", when it has them
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }

    // When FyreApp first stored it, and last received it changed
    public DateTime FetchedUtc { get; set; }
    public DateTime ChangedUtc { get; set; }

    // When a full pull last returned it; a full pull that doesn't marks it deleted
    public DateTime SeenUtc { get; set; }

    // Gone from Uptick (deleted there). Never removed from this table.
    public DateTime? DeletedUtc { get; set; }

    // When FyreApp's own tables last took it in; null = waiting to be mapped
    public DateTime? MappedUtc { get; set; }

    // A parent (e.g. an invoice) whose child records were fetched for it alone (§4.5): not fetched again until it
    // changes
    public DateTime? ChildrenFetchedUtc { get; set; }
}

// Where the migration is with one endpoint (§3.3)
public class UptickEndpointState
{
    [Key, StringLength(100)] public string Resource { get; set; } = string.Empty;

    // False for endpoints left out on purpose; they're listed but never fetched
    public bool Enabled { get; set; } = true;

    // "updatedsince" was checked against a full pull and returns exactly the records changed since,
    // so a Refresh can ask for changes only
    public bool IncrementalVerified { get; set; }
    public DateTime? IncrementalCheckedUtc { get; set; }

    // Latest "updated" seen: where a Refresh carries on from
    public DateTime? CursorUtc { get; set; }

    public DateTime? LastFullRunUtc { get; set; }
    public DateTime? LastRefreshUtc { get; set; }

    // Uptick's own count at the last full pull, and the records held here that aren't deleted
    public int? UptickCount { get; set; }
    public int CopiedCount { get; set; }

    // Of those, the ones taken into FyreApp's own tables (endpoints with a mapper)
    public int? MappedCount { get; set; }

    // Ok, Empty, Forbidden, Failed
    [StringLength(20)] public string? Status { get; set; }
    public string? LastError { get; set; }

    // Field definitions from the endpoint's OPTIONS, as last seen (to flag changes)
    public string? Schema { get; set; }
}

// One migration run and what it found, kept as the record of what was copied (§4.4)
public class UptickMigrationRun
{
    public int Id { get; set; }

    // Full, Refresh
    [StringLength(20)] public string Kind { get; set; } = string.Empty;

    // One endpoint only, or null for all
    [StringLength(100)] public string? Resource { get; set; }

    [StringLength(200)] public string? StartedBy { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }

    // Running, Completed, Cancelled, Failed, Interrupted
    [StringLength(20)] public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }

    public int Requests { get; set; }
    public int Throttled { get; set; }

    // Per endpoint: counts, status, schema changes (UptickEndpointResult[])
    public string? Report { get; set; }
}

// A file Uptick stores, copied into FyreApp's file store (§8): a document file (photo, signature, attachment;
// Uptick "documentfiles") or a report's PDF. Each is checked again on a Files run and downloaded when it's new or
// changed in Uptick.
// Photos taken in FyreApp (the field app's remark photos) are kept here too, as Source "fyreapp" (FyreAppFiles), with
// the FyreApp record they belong to (RecordType, RecordId); Files runs only look at Uptick's sources.
public class UptickFile
{
    public int Id { get; set; }

    // "documentfiles" or "reports"
    [StringLength(100)] public string Source { get; set; } = string.Empty;

    // The document file's id (a GUID) or the report's id
    [StringLength(64)] public string SourceId { get; set; } = string.Empty;

    // Where Uptick keeps it, e.g. "properties/457/photos/items/7309/20260212/28438059.jpg" or
    // "reports/9999/R-09999 P-0209 Service Report.pdf"; the last part is the file's name
    [StringLength(1000)] public string? Path { get; set; }

    // The record it belongs to, by Uptick resource and id, from where Uptick keeps it (UptickFilePaths): e.g. a
    // remark's photo is ("remarks", 55400). Null for account-level files (contracts, exports).
    [StringLength(50)] public string? RecordType { get; set; }
    public long? RecordUptickId { get; set; }

    // The FyreApp record it belongs to, for a file added in FyreApp (by its Id, e.g. a remark's)
    public int? RecordId { get; set; }

    // photo, signature, document or pdf (a report's generated PDF)
    [StringLength(20)] public string? Kind { get; set; }

    // A remark photo's "open" or "closed" (as found / once fixed)
    [StringLength(20)] public string? PhotoType { get; set; }

    // Who signed, for a customer's signature (Uptick's taskclientsignatures)
    [StringLength(300)] public string? Caption { get; set; }

    // When it was added in Uptick
    public DateTime? CreatedUtc { get; set; }

    public long? Size { get; set; }

    // Uptick's etag (an MD5 of the file for most), as last downloaded or checked
    [StringLength(100)] public string? Etag { get; set; }

    // The record's "updated" when last checked: a report's PDF is looked at again once the report changes
    public DateTime? SourceUpdatedUtc { get; set; }

    [StringLength(200)] public string? ContentType { get; set; }

    // Its key in FyreApp's file store; null until downloaded
    [StringLength(300)] public string? StorageKey { get; set; }

    // MD5 of what was stored
    [StringLength(32)] public string? Md5 { get; set; }

    public DateTime? DownloadedUtc { get; set; }
    public DateTime CheckedUtc { get; set; }

    // A report Uptick has no PDF for (yet)
    public bool NoFile { get; set; }

    // Why the last attempt failed, and how many have
    [StringLength(500)] public string? Error { get; set; }
    public int Failures { get; set; }
}
