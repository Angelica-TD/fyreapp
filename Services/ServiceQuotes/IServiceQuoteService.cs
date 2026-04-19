using FyreApp.Models;
using FyreApp.ViewModels.ServiceQuotes;

namespace FyreApp.Services.ServiceQuotes;

public enum ServiceQuoteCreateStatus
{
    Success = 1,
    ClientNotFound = 2,
    ValidationError = 3
}

public enum ServiceQuoteUpdateStatus
{
    Success = 1,
    NotFound = 2,
    ValidationError = 3
}

public enum ServiceQuoteDeleteStatus
{
    Success = 1,
    NotFound = 2
}

public sealed record ServiceQuoteCreateResult(
    ServiceQuoteCreateStatus Status,
    int? QuoteId = null,
    string? ErrorMessage = null
);

public sealed record ServiceQuoteUpdateResult(
    ServiceQuoteUpdateStatus Status,
    string? ErrorMessage = null
);

public sealed record ServiceQuoteDeleteResult(
    ServiceQuoteDeleteStatus Status
);

public interface IServiceQuoteService
{
    Task<List<ServiceQuote>> GetAllAsync(CancellationToken ct = default);
    Task<List<ServiceQuote>> GetByClientAsync(int clientId, CancellationToken ct = default);
    Task<ServiceQuote?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ServiceQuoteCreateResult> CreateAsync(CreateServiceQuoteVm vm, CancellationToken ct = default);
    Task<ServiceQuoteUpdateResult> UpdateAsync(int id, UpdateServiceQuoteRequest request, CancellationToken ct = default);
    Task<ServiceQuoteDeleteResult> DeleteAsync(int id, CancellationToken ct = default);
}
