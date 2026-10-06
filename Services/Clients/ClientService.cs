using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Clients;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Clients;

public sealed class ClientService : IClientService
{
    private readonly AppDbContext _db;

    public ClientService(AppDbContext db) => _db = db;

    public async Task<UpdateClientResult> UpdateAsync(int id, UpdateClientRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return new(ClientUpdateStatus.ValidationError, ErrorMessage: "Client name is required.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null)
            return new(ClientUpdateStatus.NotFound);

        // Uniqueness check (case-insensitive), excluding the current record. Only when renaming:
        // Uptick allows clients to share a name, so imported duplicates must stay editable.
        var renamed = !string.Equals(client.Name, name, StringComparison.OrdinalIgnoreCase);
        var duplicateExists = renamed && await _db.Clients
            .AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower(), ct);

        if (duplicateExists)
            return new(ClientUpdateStatus.DuplicateName, ErrorMessage: "A client with this name already exists.");

        client.Name = name;
        client.Active = request.Active;

        client.PrimaryContactName = request.PrimaryContactName?.Trim();
        client.PrimaryContactAddress = request.PrimaryContactAddress?.Trim();
        client.PrimaryContactEmail = request.PrimaryContactEmail?.Trim();
        client.PrimaryContactMobile = request.PrimaryContactMobile?.Trim();
        client.PrimaryContactCcEmail = request.PrimaryContactCcEmail?.Trim();

        client.BillingAddress = request.BillingAddress?.Trim();
        client.BillingAttentionTo = request.BillingAttentionTo?.Trim();
        client.BillingCcEmail = request.BillingCcEmail?.Trim();
        client.BillingEmail = request.BillingEmail?.Trim();

        client.BillingName = request.BillingName?.Trim();
        
        client.Updated = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return new(ClientUpdateStatus.Success, ClientId: client.Id);
    }

    public async Task<DeleteClientResult> DeleteAsync(int id, bool hardDelete = false, CancellationToken ct = default)
    {
        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (client is null)
            return new(ClientDeleteStatus.NotFound);

        if (hardDelete)
        {
            _db.Clients.Remove(client);
        }
        else
        {
            // Soft delete
            client.Active = false;
            client.Updated = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return new(ClientDeleteStatus.Success);
    }

    public async Task<List<Client>> GetAllAsync(bool activeOnly = true)
    {
        var query = _db.Clients.Include(c => c.Sites).OrderBy(c => c.Name);

        return activeOnly
            ? await query.Where(c => c.Active).ToListAsync()
            : await query.ToListAsync();
    }

    // The one place the list filters are applied, so the list, its download and "select all" always agree
    private IQueryable<Client> Filtered(string? search, bool? active)
    {
        var q = _db.Clients.AsQueryable();

        if (active is bool a)
            q = q.Where(c => c.Active == a);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.PrimaryContactName != null && c.PrimaryContactName.ToLower().Contains(term)) ||
                (c.ExternalId != null && c.ExternalId.ToLower() == term) ||
                (c.FyreRef != null && c.FyreRef.ToLower() == term));
        }

        return q.OrderBy(c => c.Name).ThenBy(c => c.Id);
    }

    public async Task<(int Total, List<ClientListItem> Items)> SearchAsync(
        string? search, bool? active, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Filtered(search, active).AsNoTracking();
        var total = await q.CountAsync(ct);
        var items = await q
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClientListItem(
                c.Id, c.ExternalId ?? c.FyreRef, c.Name, c.PrimaryContactName, c.PrimaryContactMobile, c.Sites.Count, c.Active))
            .ToListAsync(ct);

        return (total, items);
    }

    public async Task<byte[]> CsvAsync(string? search, bool? active, CancellationToken ct = default)
    {
        var rows = await Filtered(search, active).AsNoTracking()
            .Select(c => new
            {
                Ref = c.ExternalId ?? c.FyreRef, c.Name, c.Active, c.PrimaryContactName, c.PrimaryContactEmail, c.PrimaryContactMobile,
                c.PrimaryContactAddress, c.BillingName, c.BillingEmail, c.BillingAddress, Properties = c.Sites.Count, c.IsPlaceholder
            })
            .ToListAsync(ct);

        return FyreApp.Infrastructure.CsvExport.Build(
            new[] { "Ref", "Client", "Active", "Primary contact", "Email", "Mobile", "Address", "Billing name", "Billing email",
                    "Billing address", "Properties", "Placeholder" },
            rows.Select(r => new[]
            {
                r.Ref, r.Name, r.Active ? "Yes" : "No", r.PrimaryContactName, r.PrimaryContactEmail, r.PrimaryContactMobile,
                r.PrimaryContactAddress, r.BillingName, r.BillingEmail, r.BillingAddress, r.Properties.ToString(), r.IsPlaceholder ? "Yes" : ""
            }));
    }

    public async Task<int> SetActiveAsync(
        FyreApp.ViewModels.Lists.BulkSelection selection, string? search, bool? active, bool makeActive, CancellationToken ct = default)
    {
        var ids = selection.Ids.ToList();
        var clients = await (selection.AllMatching ? Filtered(search, active) : _db.Clients.Where(c => ids.Contains(c.Id))).ToListAsync(ct);
        foreach (var c in clients) c.Active = makeActive;
        await _db.SaveChangesAsync(ct);
        return clients.Count;
    }

    public async Task<ClientCreateResult> CreateAsync(CreateClientVm vm, CancellationToken ct = default)
    {
        var name = (vm.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
            return new(ClientCreateStatus.ValidationError, ErrorMessage: "Client name is required.");

        var existing = await _db.Clients
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower(), ct);

        if (existing is not null)
            return new(ClientCreateStatus.DuplicateName, Existing: existing);

        var client = new Client
        {
            Name = name,
            PrimaryContactName = vm.PrimaryContactName?.Trim(),
            PrimaryContactEmail = vm.PrimaryContactEmail?.Trim(),
            PrimaryContactMobile = vm.PrimaryContactMobile?.Trim(),
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync(ct);

        return new(ClientCreateStatus.Success, ClientId: client.Id);
    }

    public async Task<Client?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Clients
            .Include(c => c.Sites)
                .ThenInclude(s => s.Assets)
                    .ThenInclude(a => a.MaintenanceSchedules)
                        .ThenInclude(ms => ms.MaintenanceInterval)
            .Include(c => c.Sites)
                .ThenInclude(s => s.MaintenanceSchedules)
                    .ThenInclude(ms => ms.MaintenanceInterval)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<List<ClientTask>> GetTasksByClientAsync(int clientId, CancellationToken ct = default)
    {
        return await _db.ClientTasks
            .Where(t => t.ClientId == clientId)
            .Include(t => t.Site)
            .OrderBy(t => t.Status)
                .ThenByDescending(t => t.Priority)
                    .ThenBy(t => t.Title)
            .ToListAsync(ct);
    }

}
