using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using FyreApp.ViewModels.Assets;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Assets;

public interface IAssetListService
{
    Task<AssetListVm> SearchAsync(AssetFilter filter, CancellationToken ct = default);
    Task<byte[]> CsvAsync(AssetFilter filter, CancellationToken ct = default);

    // Edit → Set active / inactive
    Task<int> SetActiveAsync(BulkSelection selection, AssetFilter filter, bool active, CancellationToken ct = default);

    // Edit → Generate tasks: one per property, listing its selected assets
    Task<int> GenerateTasksAsync(BulkSelection selection, AssetFilter filter, BulkTaskInput input, string? userId, CancellationToken ct = default);
}

// Assets listed, filtered and edited like Uptick's Assets page
public class AssetListService : IAssetListService
{
    private readonly AppDbContext _db;
    public AssetListService(AppDbContext db) => _db = db;

    // The one place the filters are applied, so the list, its download and "select all" always agree
    private IQueryable<Asset> Filtered(AssetFilter filter)
    {
        var q = _db.Assets.AsQueryable();

        if (filter.Active is bool active)
            q = q.Where(a => a.IsActive == active);

        if (filter.AssetTypeIds.Count > 0)
        {
            var typeIds = filter.AssetTypeIds.ToList();
            q = filter.AssetTypeIsNot
                ? q.Where(a => !a.AssetTypes.Any(t => typeIds.Contains(t.Id)))
                : q.Where(a => a.AssetTypes.Any(t => typeIds.Contains(t.Id)));
        }

        if (filter.PropertyStatus.Count > 0)
        {
            var statuses = filter.PropertyStatus.Select(s => s.ToUpperInvariant()).ToList();
            q = q.Where(a => a.Site.Status != null && statuses.Contains(a.Site.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(a =>
                a.Name.ToLower().Contains(term) ||
                (a.Ref != null && a.Ref.ToLower().Contains(term)) ||
                (a.Location != null && a.Location.ToLower().Contains(term)) ||
                a.Site.Name.ToLower().Contains(term) ||
                (a.Site.ExternalId != null && a.Site.ExternalId.ToLower().Contains(term)) ||
                a.Site.Client.Name.ToLower().Contains(term));
        }

        // Newest first, as Uptick lists them: assets made in FyreApp (no Uptick ID) by Id, then Uptick IDs
        // numerically (longer number = larger ID)
        return q
            .OrderBy(a => a.ExternalId != null)
            .ThenByDescending(a => a.ExternalId == null ? a.Id : 0)
            .ThenByDescending(a => (a.ExternalId ?? "").Length)
            .ThenByDescending(a => a.ExternalId);
    }

    private IQueryable<Asset> Selected(BulkSelection selection, AssetFilter filter)
    {
        if (selection.AllMatching) return Filtered(filter);
        var ids = selection.Ids.ToList();
        return _db.Assets.Where(a => ids.Contains(a.Id));
    }

    public async Task<AssetListVm> SearchAsync(AssetFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter).AsNoTracking();
        var total = await q.CountAsync(ct);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)AssetListVm.PageSize)));
        filter.Page = page;

        var items = await q
            .Skip((page - 1) * AssetListVm.PageSize)
            .Take(AssetListVm.PageSize)
            .Select(a => new AssetListItemVm
            {
                Id = a.Id,
                Ref = a.Ref,
                Name = a.Name,
                SiteId = a.SiteId,
                PropertyRef = a.Site.ExternalId ?? a.Site.FyreRef,
                PropertyName = a.Site.Name,
                ClientId = a.Site.ClientId,
                ClientName = a.Site.Client.Name,
                ClientContact = a.Site.Client.PrimaryContactName
            })
            .ToListAsync(ct);

        var types = await _db.AssetTypes.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        return new AssetListVm
        {
            Filter = filter,
            Items = items,
            Total = total,
            AssetTypes = types.Select(t => (t.Id, t.Name)).ToList()
        };
    }

    public async Task<byte[]> CsvAsync(AssetFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(a => new
            {
                Uptick = a.ExternalId ?? a.FyreRef, a.Ref, a.Name, Types = a.AssetTypes.Select(t => t.Name).ToList(),
                a.Variant, a.Location, a.Make, a.Model, a.Size, a.Barcode, a.Compliance, a.IsActive,
                a.InstallationDate, a.LastServiceDate,
                PropertyRef = a.Site.ExternalId ?? a.Site.FyreRef, Property = a.Site.Name, Client = a.Site.Client.Name
            })
            .ToListAsync(ct);

        return CsvExport.Build(
            new[] { "Asset ID", "Ref", "Asset", "Type", "Variant", "Location", "Make", "Model", "Size", "Barcode", "Last result",
                    "Active", "Installed", "Last service", "Property ref", "Property", "Client" },
            rows.Select(r => new[]
            {
                r.Uptick, r.Ref, r.Name, string.Join(", ", r.Types), r.Variant, r.Location, r.Make, r.Model, r.Size, r.Barcode,
                r.Compliance, r.IsActive ? "Yes" : "No", CsvExport.Date(r.InstallationDate), CsvExport.Date(r.LastServiceDate),
                r.PropertyRef, r.Property, r.Client
            }));
    }

    public async Task<int> SetActiveAsync(BulkSelection selection, AssetFilter filter, bool active, CancellationToken ct = default)
    {
        var assets = await Selected(selection, filter).ToListAsync(ct);
        foreach (var a in assets) a.IsActive = active;
        await _db.SaveChangesAsync(ct);
        return assets.Count;
    }

    public async Task<int> GenerateTasksAsync(BulkSelection selection, AssetFilter filter, BulkTaskInput input, string? userId, CancellationToken ct = default)
    {
        var assets = await Selected(selection, filter).Include(a => a.Site).ToListAsync(ct);
        var tasks = BulkTaskBuilder.PerProperty(
            assets, a => a.Site,
            a => $"{(a.Ref != null ? a.Ref + " - " : "")}{a.Name}{(string.IsNullOrWhiteSpace(a.Location) ? "" : $" ({a.Location})")}",
            input, "Asset works", userId);
        _db.ClientTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);
        return tasks.Count;
    }
}
