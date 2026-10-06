using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface IPropertyListService
{
    Task<PropertyListVm> SearchAsync(PropertyFilter filter, CancellationToken ct = default);
    Task<byte[]> CsvAsync(PropertyFilter filter, CancellationToken ct = default);

    // Edit → Change status. Uptick status (ACTIVE, SETUP, ONHOLD, INACTIVE); only INACTIVE makes a property inactive.
    Task<int> SetStatusAsync(BulkSelection selection, PropertyFilter filter, string status, CancellationToken ct = default);

    // Edit → Generate tasks: one per selected property
    Task<int> GenerateTasksAsync(BulkSelection selection, PropertyFilter filter, BulkTaskInput input, string? userId, CancellationToken ct = default);
}

// Properties listed, filtered and edited like Uptick's Properties page
public class PropertyListService : IPropertyListService
{
    private readonly AppDbContext _db;
    public PropertyListService(AppDbContext db) => _db = db;

    // The one place the filters are applied, so the list, its download and "select all" always agree
    private IQueryable<Site> Filtered(PropertyFilter filter)
    {
        var q = _db.Sites.AsQueryable();

        if (filter.Status.Count > 0)
        {
            var statuses = filter.Status.Select(s => s.ToUpperInvariant()).ToList();
            q = q.Where(s => s.Status != null && statuses.Contains(s.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.ExternalId != null && s.ExternalId.ToLower().Contains(term)) ||
                (s.AddressDisplay != null && s.AddressDisplay.ToLower().Contains(term)) ||
                s.Client.Name.ToLower().Contains(term));
        }

        // Newest first, as Uptick lists them
        return q.OrderBy(s => s.Created == null).ThenByDescending(s => s.Created).ThenByDescending(s => s.Id);
    }

    private IQueryable<Site> Selected(BulkSelection selection, PropertyFilter filter)
    {
        if (selection.AllMatching) return Filtered(filter);
        var ids = selection.Ids.ToList();
        return _db.Sites.Where(s => ids.Contains(s.Id));
    }

    public async Task<PropertyListVm> SearchAsync(PropertyFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter).AsNoTracking();
        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);

        var items = await q
            .Skip((filter.Page - 1) * ListFilters.PageSize)
            .Take(ListFilters.PageSize)
            .Select(s => new PropertyListItemVm
            {
                Id = s.Id,
                Ref = s.ExternalId ?? s.FyreRef,
                Status = s.Status,
                Created = s.Created,
                Name = s.Name,
                State = s.State,
                ClientId = s.ClientId,
                ClientName = s.Client.Name,
                ClientContact = s.Client.PrimaryContactName
            })
            .ToListAsync(ct);

        return new PropertyListVm { Filter = filter, Items = items, Total = total };
    }

    public async Task<byte[]> CsvAsync(PropertyFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(s => new
            {
                Ref = s.ExternalId ?? s.FyreRef, s.Status, s.Created, s.Name, s.AddressDisplay, s.Suburb, s.State, s.Postcode,
                Client = s.Client.Name, Contact = s.Client.PrimaryContactName, s.IsPlaceholder
            })
            .ToListAsync(ct);

        return CsvExport.Build(
            new[] { "Ref", "Status", "Created", "Property", "Address", "Suburb", "State", "Postcode", "Client", "Client contact", "Placeholder" },
            rows.Select(r => new[]
            {
                r.Ref, ListFilters.PropertyStatusLabel(r.Status), CsvExport.Date(r.Created?.ToSydney()), r.Name, r.AddressDisplay,
                r.Suburb, r.State, r.Postcode, r.Client, r.Contact, r.IsPlaceholder ? "Yes" : ""
            }));
    }

    public async Task<int> SetStatusAsync(BulkSelection selection, PropertyFilter filter, string status, CancellationToken ct = default)
    {
        status = status.ToUpperInvariant();
        if (!ListFilters.PropertyStatuses.Contains(status))
            throw new ArgumentException($"Unknown property status '{status}'.", nameof(status));

        var sites = await Selected(selection, filter).ToListAsync(ct);
        foreach (var s in sites)
        {
            s.Status = status;
            s.Active = status != "INACTIVE";
        }
        await _db.SaveChangesAsync(ct);
        return sites.Count;
    }

    public async Task<int> GenerateTasksAsync(BulkSelection selection, PropertyFilter filter, BulkTaskInput input, string? userId, CancellationToken ct = default)
    {
        var sites = await Selected(selection, filter).ToListAsync(ct);
        var tasks = BulkTaskBuilder.PerProperty(sites, s => s, _ => null, input, "Property task", userId);
        _db.ClientTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);
        return tasks.Count;
    }
}
