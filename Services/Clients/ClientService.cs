using FyreApp.Data;
using FyreApp.Models;
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

        // Uniqueness check (case-insensitive), excluding the current record
        var duplicateExists = await _db.Clients
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
}
