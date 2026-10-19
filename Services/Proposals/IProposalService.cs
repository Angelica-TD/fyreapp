using FyreApp.Models;
using FyreApp.ViewModels.Proposals;

namespace FyreApp.Services.Proposals;

public enum ProposalCreateStatus
{
    Success = 1,
    ClientNotFound = 2,
    ValidationError = 3
}

public enum ProposalUpdateStatus
{
    Success = 1,
    NotFound = 2,
    ValidationError = 3
}

public enum ProposalDeleteStatus
{
    Success = 1,
    NotFound = 2
}

public enum PrepareQuoteForSendStatus
{
    Success = 1,
    NotFound = 2,
    NoClientEmail = 3
}

public sealed record ProposalCreateResult(
    ProposalCreateStatus Status,
    int? QuoteId = null,
    string? ErrorMessage = null
);

public sealed record ProposalUpdateResult(
    ProposalUpdateStatus Status,
    string? ErrorMessage = null
);

public sealed record ProposalDeleteResult(
    ProposalDeleteStatus Status
);

public sealed record PrepareQuoteForSendResult(
    PrepareQuoteForSendStatus Status,
    Guid? Token = null,
    string? ClientEmail = null,
    string? ClientName = null,
    Proposal? Quote = null,
    string? ErrorMessage = null
);

public interface IProposalService
{
    Task<List<Proposal>> GetAllAsync(CancellationToken ct = default);
    Task<List<Proposal>> GetByClientAsync(int clientId, CancellationToken ct = default);
    Task<Proposal?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Proposal?> GetByTokenAsync(Guid token, CancellationToken ct = default);
    Task<ProposalCreateResult> CreateAsync(CreateProposalVm vm, int clientId, int? siteId, CancellationToken ct = default);
    Task<ProposalUpdateResult> UpdateAsync(int id, UpdateProposalRequest request, CancellationToken ct = default);
    Task<ProposalDeleteResult> DeleteAsync(int id, CancellationToken ct = default);
    Task<PrepareQuoteForSendResult> PrepareForSendAsync(int id, CancellationToken ct = default);
    Task<ProposalUpdateResult> ApproveAsync(Guid token, CancellationToken ct = default);
    Task<ProposalUpdateResult> DeclineAsync(Guid token, CancellationToken ct = default);
}
