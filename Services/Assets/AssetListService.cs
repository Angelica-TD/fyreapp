using FyreApp.Data;
using FyreApp.ViewModels.Assets;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Assets;

public interface IAssetListService
{
    Task<AssetListVm> SearchAsync(AssetFilter filter, CancellationToken ct = default);
}

// Assets listed and filtered like Uptick's Assets page
public class AssetListService : IAssetListService
{
    private readonly AppDbContext _db;
    public AssetListService(AppDbContext db) => _db = db;

    public async Task<AssetListVm> SearchAsync(AssetFilter filter, CancellationToken ct = default)
    {
        var q = _db.Assets.AsNoTracking().AsQueryable();

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

        var total = await q.CountAsync(ct);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)AssetListVm.PageSize)));
        filter.Page = page;

        // Newest first, as Uptick lists them: assets made in FyreApp (no Uptick ID) by Id, then Uptick IDs
        // numerically (longer number = larger ID)
        var items = await q
            .OrderBy(a => a.ExternalId != null)
            .ThenByDescending(a => a.ExternalId == null ? a.Id : 0)
            .ThenByDescending(a => (a.ExternalId ?? "").Length)
            .ThenByDescending(a => a.ExternalId)
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
}
