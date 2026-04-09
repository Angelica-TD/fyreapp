using FyreApp.Models;
using FyreApp.ViewModels.Clients;

namespace FyreApp.Services.Clients;

public enum ClientUpdateStatus
{
    Success = 1,
    NotFound = 2,
    ValidationError = 3,
    DuplicateName = 4
}

public enum ClientDeleteStatus
{
    Success = 1,
    NotFound = 2,
}

public enum ClientCreateStatus
{
    Success = 1,
    DuplicateName = 2,
    ValidationError = 3
}

public sealed record ClientCreateResult(
    ClientCreateStatus Status,
    int? ClientId = null,
    string? ErrorMessage = null,
    Client? Existing = null
);

public sealed record UpdateClientRequest
{
    public string Name { get; set; } = string.Empty;

    // Auditing / status
    public DateTime? Updated { get; set; }
    public bool Active { get; set; } = true;

    // Primary contact
    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactEmail { get; set; }
    public string? PrimaryContactMobile { get; set; }
    public string? PrimaryContactCcEmail { get; set; }
    public string? PrimaryContactAddress { get; set; }

    // Billing
    public string? BillingName { get; set; }
    public string? BillingAttentionTo { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingCcEmail { get; set; }

    //the exported data only has one field for billing address
    public string? BillingAddress { get; set; }
}

public sealed record UpdateClientResult(
    ClientUpdateStatus Status,
    int? ClientId = null,
    string? ErrorMessage = null
);

public sealed record DeleteClientResult(
    ClientDeleteStatus Status
);

public interface IClientService
{
    Task<UpdateClientResult> UpdateAsync(int id, UpdateClientRequest request, CancellationToken ct = default);
    Task<DeleteClientResult> DeleteAsync(int id, bool hardDelete = false, CancellationToken ct = default);
    Task<List<Client>> GetAllAsync(bool activeOnly = true);
    Task<ClientCreateResult> CreateAsync(CreateClientVm vm, CancellationToken ct = default);
    Task<Client?> GetByIdAsync(int id, CancellationToken ct = default);
}
