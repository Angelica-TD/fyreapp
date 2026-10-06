using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.DataReset;

public interface IDataResetService
{
    Task<bool> IsEnabledAsync(CancellationToken ct = default);
    Task SetEnabledAsync(bool enabled, string? userName, CancellationToken ct = default);

    // Row counts of everything the reset would delete, in deletion order.
    Task<IReadOnlyList<(string Table, int Count)>> GetCountsAsync(CancellationToken ct = default);

    // Deletes all client data and switches the reset off again. Returns rows deleted per table.
    Task<IReadOnlyList<(string Table, int Count)>> ResetAsync(string? userName, CancellationToken ct = default);
}

// Wipes clients and everything that hangs off them (sites, assets, schedules, tasks, quotes, ...).
// Reference data (users, intervals, asset types, service offerings/types, asset catalogue) is kept.
public class DataResetService : IDataResetService
{
    public const string EnabledKey = "DataReset:Enabled";
    public const string ConfirmPhrase = "DELETE ALL CLIENTS";

    private readonly AppDbContext _db;
    private readonly ILogger<DataResetService> _logger;

    public DataResetService(AppDbContext db, ILogger<DataResetService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var value = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == EnabledKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return bool.TryParse(value, out var enabled) && enabled;
    }

    public async Task SetEnabledAsync(bool enabled, string? userName, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FindAsync(new object[] { EnabledKey }, ct);
        if (setting == null)
        {
            setting = new AppSetting { Key = EnabledKey };
            _db.AppSettings.Add(setting);
        }

        setting.Value = enabled.ToString();
        setting.UpdatedUtc = DateTime.UtcNow;
        setting.UpdatedBy = userName;

        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Data reset {State} by {User}", enabled ? "ENABLED" : "disabled", userName);
    }

    public async Task<IReadOnlyList<(string Table, int Count)>> GetCountsAsync(CancellationToken ct = default) =>
        new List<(string, int)>
        {
            ("Quote line items", await _db.QuoteLineItems.CountAsync(ct)),
            ("Quotes", await _db.Quotes.CountAsync(ct)),
            ("Service quotes", await _db.ServiceQuotes.CountAsync(ct)),
            ("Tasks", await _db.ClientTasks.CountAsync(ct)),
            ("Maintenance history", await _db.MaintenanceHistory.CountAsync(ct)),
            ("Routines", await _db.RoutineOccurrences.CountAsync(ct)),
            ("Maintenance schedules", await _db.MaintenanceSchedules.CountAsync(ct)),
            ("Defects", await _db.Defects.CountAsync(ct)),
            ("Service reports", await _db.ServiceReports.CountAsync(ct)),
            ("Property contacts", await _db.SiteContacts.CountAsync(ct)),
            ("Assets", await _db.Assets.CountAsync(ct)),
            ("Properties", await _db.Sites.CountAsync(ct)),
            ("Clients", await _db.Clients.CountAsync(ct))
        };

    public async Task<IReadOnlyList<(string Table, int Count)>> ResetAsync(string? userName, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
            throw new InvalidOperationException("Data reset is disabled.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Explicit child-first order: QuoteLineItem -> Asset is ON DELETE RESTRICT, so relying on
        // the Clients cascade alone would fail whenever a quote references an asset.
        var deleted = new List<(string, int)>
        {
            ("Quote line items", await _db.QuoteLineItems.ExecuteDeleteAsync(ct)),
            ("Quotes", await _db.Quotes.ExecuteDeleteAsync(ct)),
            ("Service quotes", await _db.ServiceQuotes.ExecuteDeleteAsync(ct)),
            ("Tasks", await _db.ClientTasks.ExecuteDeleteAsync(ct)),
            ("Maintenance history", await _db.MaintenanceHistory.ExecuteDeleteAsync(ct)),
            ("Routines", await _db.RoutineOccurrences.ExecuteDeleteAsync(ct)),
            ("Maintenance schedules", await _db.MaintenanceSchedules.ExecuteDeleteAsync(ct)),
            ("Defects", await _db.Defects.ExecuteDeleteAsync(ct)),
            ("Service reports", await _db.ServiceReports.ExecuteDeleteAsync(ct)),
            ("Property contacts", await _db.SiteContacts.ExecuteDeleteAsync(ct)),
            ("Assets", await _db.Assets.ExecuteDeleteAsync(ct)),
            ("Properties", await _db.Sites.ExecuteDeleteAsync(ct)),
            ("Clients", await _db.Clients.ExecuteDeleteAsync(ct))
        };

        // One-shot: switch the reset off again in the same transaction
        await _db.AppSettings
            .Where(s => s.Key == EnabledKey)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Value, false.ToString())
                .SetProperty(x => x.UpdatedUtc, DateTime.UtcNow)
                .SetProperty(x => x.UpdatedBy, userName), ct);

        await tx.CommitAsync(ct);

        _logger.LogWarning("Data reset performed by {User}: {Deleted}", userName,
            string.Join(", ", deleted.Select(d => $"{d.Item1}={d.Item2}")));

        return deleted;
    }
}
