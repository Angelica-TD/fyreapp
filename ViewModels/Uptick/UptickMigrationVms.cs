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

    public int Copied => Endpoints.Sum(e => e.CopiedCount);
}

public sealed class UptickRunReportVm
{
    public UptickMigrationRun Run { get; init; } = new();
    public IReadOnlyList<UptickEndpointResult> Results { get; init; } = [];
}
