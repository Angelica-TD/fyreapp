using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Routines;
using FyreApp.Services.Timeline;
using FyreApp.ViewModels.Properties;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Properties;

public interface IPropertyPageService
{
    // The property page with what its tab shows; null when there's no such property
    Task<PropertyPageVm?> LoadAsync(int id, PropertyPageQuery query, CancellationToken ct = default);
}

// What the page was asked for: the tab, plus that tab's filters
public record PropertyPageQuery(
    string? Tab, int? Year = null, string? AssetActive = null, int? AssetType = null, string? AssetSearch = null,
    bool ShowClosedRemarks = false, bool NotesOnly = false);

// Loads only what the open tab needs, as Uptick's tabs are separate pages
public class PropertyPageService : IPropertyPageService
{
    private readonly AppDbContext _db;
    private readonly IRoutineListService _routines;
    private readonly ITimelineService _timeline;

    public PropertyPageService(AppDbContext db, IRoutineListService routines, ITimelineService timeline)
    {
        _db = db;
        _routines = routines;
        _timeline = timeline;
    }

    public async Task<PropertyPageVm?> LoadAsync(int id, PropertyPageQuery query, CancellationToken ct = default)
    {
        var tab = PropertyPageVm.NormaliseTab(query.Tab);

        var site = await _db.Properties.AsNoTracking()
            .Include(s => s.Client).ThenInclude(c => c.Contacts)
            .Include(s => s.Contacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (site == null) return null;

        // Uptick's severity: higher is worse (10 critical defect … 0 informational)
        var openRemarks = _db.Remarks.AsNoTracking().Where(d => d.PropertyId == id && d.Active);
        var openCount = await openRemarks.CountAsync(ct);
        var mostSevere = await openRemarks.OrderByDescending(d => d.Severity ?? -1).FirstOrDefaultAsync(ct);

        return tab switch
        {
            "assets" => await AssetsAsync(site, query, openCount, mostSevere, ct),
            "remarks" => new PropertyPageVm
            {
                Property = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                ShowClosedRemarks = query.ShowClosedRemarks,
                Remarks = await _db.Remarks.AsNoTracking()
                    .Include(d => d.Asset)
                    .Where(d => d.PropertyId == id && (query.ShowClosedRemarks || d.Active))
                    .OrderByDescending(d => d.Severity ?? -1).ThenByDescending(d => d.RaisedUtc)
                    .ToListAsync(ct)
            },
            "routines" => new PropertyPageVm
            {
                Property = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Routines = await _routines.ForPropertyAsync(id, Math.Clamp(query.Year ?? DateTime.UtcNow.ToSydney().Year, 2000, 2100), ct)
            },
            "records" => new PropertyPageVm
            {
                Property = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Reports = await _db.Reports.AsNoTracking()
                    .Where(r => r.PropertyId == id)
                    .OrderByDescending(r => r.IssuedDate ?? r.InspectedDate)
                    .ToListAsync(ct),
                // Uptick's quotes, at the property; schedules matched on its ref
                DefectQuotes = await _db.DefectQuotes.AsNoTracking()
                    .Include(q => q.ClientTask)
                    .Where(q => q.PropertyId == id)
                    .OrderByDescending(q => q.Date)
                    .ToListAsync(ct),
                ServiceQuotes = await _db.ServiceQuotes.AsNoTracking()
                    .Where(q => q.PropertyId == id)
                    .OrderByDescending(q => q.Created)
                    .ToListAsync(ct),
                Schedules = site.ExternalId == null ? [] : await _db.PropertySchedules.AsNoTracking()
                    .Where(s => s.PropertyRef == site.ExternalId)
                    .OrderByDescending(s => s.Date)
                    .ToListAsync(ct)
            },
            "tasks" => new PropertyPageVm
            {
                Property = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Tasks = await _db.ClientTasks.AsNoTracking()
                    .Include(t => t.AssignedTo)
                    .Where(t => t.PropertyId == id)
                    // Open work first, then the latest due
                    .OrderBy(t => t.Status == ClientTaskStatus.Completed || t.Status == ClientTaskStatus.Cancelled)
                    .ThenByDescending(t => t.DueDateUtc)
                    .ToListAsync(ct)
            },
            "billing" => await BillingAsync(site, openCount, mostSevere, ct),
            _ => new PropertyPageVm
            {
                Property = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Timeline = await _timeline.ForPropertyAsync(site, query.NotesOnly, ct),
                NotesOnly = query.NotesOnly,
                ExtraFields = await FyreApp.Services.Imports.UptickReferenceValues.ExtraFieldsAsync(_db, ExtraField.PropertyContentType, site.UptickData, ct)
            }
        };
    }

    // Billing tab: the property's and client's billing cards (named by ID in their Uptick rows), the property's
    // billing contracts with their lines, and its invoices (newest first) with any credit notes
    private async Task<PropertyPageVm> BillingAsync(Property site, int openCount, Remark? mostSevere, CancellationToken ct)
    {
        async Task<BillingCard?> Card(string? externalId) => externalId == null ? null
            : await _db.BillingCards.AsNoTracking().FirstOrDefaultAsync(c => c.ExternalId == externalId, ct);

        var contracts = site.ExternalId == null ? [] : await _db.BillingContracts.AsNoTracking()
            .Where(c => c.PropertyRef == site.ExternalId)
            .OrderByDescending(c => c.Active).ThenByDescending(c => c.StartDate)
            .ToListAsync(ct);
        var contractIds = contracts.Select(c => c.ExternalId).ToList();
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.PropertyId == site.Id)
            .OrderByDescending(i => i.Date).ThenByDescending(i => i.Number)
            .Take(PropertyPageVm.InvoicesShown)
            .ToListAsync(ct);
        var invoiceIds = invoices.Select(i => (int?)i.Id).ToList();

        return new PropertyPageVm
        {
            Property = site, Tab = "billing", OpenRemarks = openCount, MostSevereRemark = mostSevere,
            PropertyBillingCard = await Card(FyreApp.Services.Imports.UptickJson.Value(site.UptickData, "Billing Card ID")),
            ClientBillingCard = await Card(FyreApp.Services.Imports.UptickJson.Value(site.Client.UptickData, "Billingcard ID")),
            BillingContracts = contracts,
            BillingContractLines = await _db.BillingContractLineItems.AsNoTracking()
                .Where(l => contractIds.Contains(l.ContractExternalId))
                .OrderBy(l => l.Id)
                .ToListAsync(ct),
            Invoices = invoices,
            InvoiceCount = await _db.Invoices.CountAsync(i => i.PropertyId == site.Id, ct),
            CreditNotes = await _db.CreditNotes.AsNoTracking()
                .Where(c => invoiceIds.Contains(c.InvoiceId))
                .ToListAsync(ct)
        };
    }

    private async Task<PropertyPageVm> AssetsAsync(Property site, PropertyPageQuery query, int openCount, Remark? mostSevere, CancellationToken ct)
    {
        var all = await _db.Assets.AsNoTracking()
            .Include(a => a.AssetTypes)
            .Where(a => a.PropertyId == site.Id)
            .ToListAsync(ct);

        // Uptick's default: active assets, any type
        var active = query.AssetActive ?? "yes";
        var search = query.AssetSearch?.Trim();
        var shown = all
            .Where(a => active switch { "yes" => a.IsActive, "no" => !a.IsActive, _ => true })
            .Where(a => query.AssetType == null || a.AssetTypes.Any(t => t.Id == query.AssetType))
            .Where(a => string.IsNullOrEmpty(search) ||
                        new[] { a.Name, a.Ref, a.Location, a.Variant, a.Barcode }
                            .Any(v => v != null && v.Contains(search, StringComparison.OrdinalIgnoreCase)))
            // Uptick's inspection order, then by label
            .OrderBy(a => a.InspectionOrder ?? int.MaxValue).ThenBy(a => a.Name)
            .ToList();

        return new PropertyPageVm
        {
            Property = site, Tab = "assets", OpenRemarks = openCount, MostSevereRemark = mostSevere,
            Assets = shown,
            AssetTypes = all.SelectMany(a => a.AssetTypes).DistinctBy(t => t.Id).OrderBy(t => t.Name).Select(t => (t.Id, t.Name)).ToList(),
            AssetActive = active,
            AssetTypeId = query.AssetType,
            AssetSearch = search
        };
    }

}
