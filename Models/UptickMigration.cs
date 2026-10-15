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
