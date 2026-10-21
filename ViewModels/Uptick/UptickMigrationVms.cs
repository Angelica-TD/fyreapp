using FyreApp.Models;
using FyreApp.Services.Uptick.Migration;

namespace FyreApp.ViewModels.Uptick;

public sealed class UptickMigrationPageVm
{
    public bool Configured { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public int MaxRequestsPerMinute { get; init; }
    public int PageSize { get; init; }

    public UptickRunStatus? Current { get; init; }
    public IReadOnlyList<UptickEndpointState> Endpoints { get; init; } = [];
    public IReadOnlyList<UptickMigrationRun> Runs { get; init; } = [];

    // Endpoints mapped into FyreApp's own tables
    public IReadOnlyList<string> Mapped { get; init; } = [];

    public int Copied => Endpoints.Sum(e => e.CopiedCount);

    // Uptick's files (§8): where they're kept, and how far the copy is per kind
    public bool StorageConfigured { get; init; }
    public string Storage { get; init; } = string.Empty;
    public IReadOnlyList<UptickFileCounts> Files { get; init; } = [];

    // Sending email (proposals, login emails): set up or not, and a test send to the signed-in user
    public bool EmailConfigured { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? TestEmailTo { get; init; }
}

// One kind of file: listed in the raw copy, downloaded (and their size), without a file in Uptick, failing
public sealed record UptickFileCounts(string Source, string Label, int Listed, int Downloaded, long Bytes, int NoFile, int Failing);

public sealed class UptickRunReportVm
{
    public UptickMigrationRun Run { get; init; } = new();
    public IReadOnlyList<UptickEndpointResult> Results { get; init; } = [];
}
