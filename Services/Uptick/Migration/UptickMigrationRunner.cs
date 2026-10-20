using System.Threading.Channels;
using FyreApp.Data;
using FyreApp.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Migration;

// Runs migrations in the background (a Full run takes ~12 minutes), one at a time, and streams progress to the
// admin page over SignalR (group ProgressGroup, event "uptickProgress").
public interface IUptickMigrationRunner
{
    // The run in progress, or null
    UptickRunStatus? Current { get; }

    // False when a run is already going. A limit caps a Files run at that many files of each kind (a trial).
    bool TryStart(UptickRunKind kind, string? resource, string? startedBy, int? limit = null);

    void Cancel();
}

public sealed record UptickRunStatus(UptickRunKind Kind, string? Resource, DateTime StartedUtc, UptickProgress? Progress, bool Cancelling);

public class UptickMigrationRunner : BackgroundService, IUptickMigrationRunner
{
    public const string ProgressGroup = "uptick-migration";

    private sealed record Request(UptickRunKind Kind, string? Resource, string? StartedBy, int? Limit);

    private readonly Channel<Request> _queue = Channel.CreateBounded<Request>(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<ImportProgressHub> _hub;
    private readonly ILogger<UptickMigrationRunner> _logger;
    private readonly object _lock = new();
    private UptickRunStatus? _current;
    private CancellationTokenSource? _cancel;

    public UptickMigrationRunner(IServiceScopeFactory scopes, IHubContext<ImportProgressHub> hub, ILogger<UptickMigrationRunner> logger)
    {
        _scopes = scopes;
        _hub = hub;
        _logger = logger;
    }

    public UptickRunStatus? Current
    {
        get { lock (_lock) return _current; }
    }

    public bool TryStart(UptickRunKind kind, string? resource, string? startedBy, int? limit = null)
    {
        lock (_lock)
        {
            if (_current != null) return false;
            if (!_queue.Writer.TryWrite(new Request(kind, resource, startedBy, limit))) return false;
            _current = new UptickRunStatus(kind, resource, DateTime.UtcNow, null, false);
            return true;
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            if (_current == null) return;
            _current = _current with { Cancelling = true };
            _cancel?.Cancel();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MarkInterruptedRunsAsync(stoppingToken);

        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_lock)
            {
                _cancel = cancel;
                if (_current?.Cancelling == true) cancel.Cancel();
            }

            try
            {
                using var scope = _scopes.CreateScope();
                var migrator = scope.ServiceProvider.GetRequiredService<UptickMigrator>();
                var progress = new Progress<UptickProgress>(p =>
                {
                    lock (_lock)
                        if (_current != null) _current = _current with { Progress = p };
                    _ = _hub.Clients.Group(ProgressGroup).SendAsync("uptickProgress", p, CancellationToken.None);
                });

                var run = await migrator.RunAsync(request.Kind, request.Resource, request.StartedBy, progress, cancel.Token, request.Limit);
                _logger.LogInformation("Uptick migration run {RunId} {Status}: {Requests} requests", run.Id, run.Status, run.Requests);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Uptick migration run failed to start or finish");
            }
            finally
            {
                lock (_lock)
                {
                    _current = null;
                    _cancel = null;
                }
                _ = _hub.Clients.Group(ProgressGroup).SendAsync("uptickFinished", CancellationToken.None);
            }
        }
    }

    // A run still "Running" at startup was cut off by a restart or deploy
    private async Task MarkInterruptedRunsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stale = await db.UptickMigrationRuns.Where(r => r.Status == "Running").ToListAsync(ct);
            foreach (var run in stale)
            {
                run.Status = "Interrupted";
                run.FinishedUtc ??= DateTime.UtcNow;
            }
            if (stale.Count > 0) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Couldn't check for interrupted Uptick migration runs");
        }
    }
}
