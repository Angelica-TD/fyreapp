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

    // Row counts of the reference data the reset keeps (it doesn't change between imports)
    Task<IReadOnlyList<(string Table, int Count)>> GetKeptCountsAsync(CancellationToken ct = default);

    // Deletes all client data and switches the reset off again. Returns rows deleted per table.
    Task<IReadOnlyList<(string Table, int Count)>> ResetAsync(string? userName, CancellationToken ct = default);
}

// Wipes clients and everything that hangs off them (sites, assets, schedules, tasks, quotes, ...).
// Reference data, which doesn't change between imports, is kept: logins and Uptick users, contractors and suppliers,
// asset / remark / routine types and levels, intervals, service offerings and types, the asset catalogue and settings.
// Nothing it deletes cascades into those tables.
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
            ("Task assets", await _db.ServiceTasks.CountAsync(ct)),
            ("Tasks", await _db.ClientTasks.CountAsync(ct)),
            ("Maintenance history", await _db.MaintenanceHistory.CountAsync(ct)),
            ("Routines", await _db.Routines.CountAsync(ct)),
            ("Routine services", await _db.RoutineServices.CountAsync(ct)),
            ("Maintenance schedules", await _db.MaintenanceSchedules.CountAsync(ct)),
            ("Defects", await _db.Remarks.CountAsync(ct)),
            ("Service reports", await _db.Reports.CountAsync(ct)),
            ("Property contacts", await _db.PropertyContacts.CountAsync(ct)),
            ("Client contacts", await _db.ClientContacts.CountAsync(ct)),
            ("Timeline events", await _db.TimelineEvents.CountAsync(ct)),
            ("Invoices", await _db.Invoices.CountAsync(ct)),
            ("Credit notes", await _db.CreditNotes.CountAsync(ct)),
            ("Billing contract line items", await _db.BillingContractLineItems.CountAsync(ct)),
            ("Billing contracts", await _db.BillingContracts.CountAsync(ct)),
            ("Billing cards", await _db.BillingCards.CountAsync(ct)),
            ("Defect quotes (Uptick)", await _db.DefectQuotes.CountAsync(ct)),
            ("Sales quotes (Uptick)", await _db.SalesQuotes.CountAsync(ct)),
            ("Property schedules", await _db.PropertySchedules.CountAsync(ct)),
            ("Form responses", await _db.FormResponses.CountAsync(ct)),
            ("Assets", await _db.Assets.CountAsync(ct)),
            ("Properties", await _db.Properties.CountAsync(ct)),
            ("Clients", await _db.Clients.CountAsync(ct))
        };

    public async Task<IReadOnlyList<(string Table, int Count)>> GetKeptCountsAsync(CancellationToken ct = default) =>
        new List<(string, int)>
        {
            ("FyreApp logins", await _db.Users.CountAsync(ct)),
            ("Uptick users", await _db.UptickUsers.CountAsync(ct)),
            ("Contractors", await _db.Contractors.CountAsync(ct)),
            ("Suppliers", await _db.Suppliers.CountAsync(ct)),
            ("Asset types", await _db.AssetTypes.CountAsync(ct)),
            ("Asset type variants", await _db.AssetTypeVariants.CountAsync(ct)),
            ("Remark types", await _db.RemarkTypes.CountAsync(ct)),
            ("Routine service types", await _db.RoutineServiceTypes.CountAsync(ct)),
            ("Routine service levels", await _db.RoutineServiceLevels.CountAsync(ct)),
            ("Maintenance intervals", await _db.MaintenanceIntervals.CountAsync(ct)),
            ("Service offerings", await _db.ServiceOfferings.CountAsync(ct)),
            ("Service types", await _db.ServiceTypes.CountAsync(ct)),
            ("Asset catalogue", await _db.AssetCatalogue.CountAsync(ct)),
            ("Security groups", await _db.SecurityGroups.CountAsync(ct)),
            ("Extra fields", await _db.ExtraFields.CountAsync(ct)),
            ("Prompt questions", await _db.PromptQuestions.CountAsync(ct)),
            ("Accreditations", await _db.Accreditations.CountAsync(ct)),
            ("Products", await _db.Products.CountAsync(ct)),
            ("Warehouses", await _db.Warehouses.CountAsync(ct)),
            ("Stock levels", await _db.StockLevels.CountAsync(ct))
        };

    public async Task<IReadOnlyList<(string Table, int Count)>> ResetAsync(string? userName, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
            throw new InvalidOperationException("Data reset is disabled.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Each statement covers a whole table: the ~1 M timeline events take longer than the default 30 s
        if (_db.Database.IsRelational()) _db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));

        // Explicit child-first order: QuoteLineItem -> Asset is ON DELETE RESTRICT, so relying on
        // the Clients cascade alone would fail whenever a quote references an asset.
        var deleted = new List<(string, int)>
        {
            ("Quote line items", await _db.QuoteLineItems.ExecuteDeleteAsync(ct)),
            ("Quotes", await _db.Quotes.ExecuteDeleteAsync(ct)),
            ("Service quotes", await _db.ServiceQuotes.ExecuteDeleteAsync(ct)),
            ("Task assets", await _db.ServiceTasks.ExecuteDeleteAsync(ct)),
            ("Tasks", await _db.ClientTasks.ExecuteDeleteAsync(ct)),
            ("Maintenance history", await _db.MaintenanceHistory.ExecuteDeleteAsync(ct)),
            ("Routines", await _db.Routines.ExecuteDeleteAsync(ct)),
            ("Routine services", await _db.RoutineServices.ExecuteDeleteAsync(ct)),
            ("Maintenance schedules", await _db.MaintenanceSchedules.ExecuteDeleteAsync(ct)),
            ("Defects", await _db.Remarks.ExecuteDeleteAsync(ct)),
            ("Service reports", await _db.Reports.ExecuteDeleteAsync(ct)),
            ("Property contacts", await _db.PropertyContacts.ExecuteDeleteAsync(ct)),
            ("Client contacts", await _db.ClientContacts.ExecuteDeleteAsync(ct)),
            ("Timeline events", await _db.TimelineEvents.ExecuteDeleteAsync(ct)),
            ("Invoices", await _db.Invoices.ExecuteDeleteAsync(ct)),
            ("Credit notes", await _db.CreditNotes.ExecuteDeleteAsync(ct)),
            ("Billing contract line items", await _db.BillingContractLineItems.ExecuteDeleteAsync(ct)),
            ("Billing contracts", await _db.BillingContracts.ExecuteDeleteAsync(ct)),
            ("Billing cards", await _db.BillingCards.ExecuteDeleteAsync(ct)),
            ("Defect quotes (Uptick)", await _db.DefectQuotes.ExecuteDeleteAsync(ct)),
            ("Sales quotes (Uptick)", await _db.SalesQuotes.ExecuteDeleteAsync(ct)),
            ("Property schedules", await _db.PropertySchedules.ExecuteDeleteAsync(ct)),
            ("Form responses", await _db.FormResponses.ExecuteDeleteAsync(ct)),
            ("Assets", await _db.Assets.ExecuteDeleteAsync(ct)),
            ("Properties", await _db.Properties.ExecuteDeleteAsync(ct)),
            ("Clients", await _db.Clients.ExecuteDeleteAsync(ct))
        };

        // Uptick's copies of what was removed are mapped again by the next migration run
        await _db.UptickRecords
            .Where(r => r.MappedUtc != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.MappedUtc, (DateTime?)null), ct);

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
