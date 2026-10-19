using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Proposals;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Proposals;

public sealed class ProposalService : IProposalService
{
    private readonly AppDbContext _db;

    public ProposalService(AppDbContext db) => _db = db;

    public async Task<List<Proposal>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Proposals
            .Include(q => q.Client)
            .Include(q => q.Property)
            .OrderByDescending(q => q.CreatedUtc)
            .ToListAsync(ct);
    }

    public async Task<List<Proposal>> GetByClientAsync(int clientId, CancellationToken ct = default)
    {
        return await _db.Proposals
            .Where(q => q.ClientId == clientId)
            .OrderByDescending(q => q.CreatedUtc)
            .ToListAsync(ct);
    }

    public async Task<Proposal?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Proposals
            .Include(q => q.Client)
            .Include(q => q.Property)
            .Include(q => q.MaintenanceInterval)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<Proposal?> GetByTokenAsync(Guid token, CancellationToken ct = default)
    {
        return await _db.Proposals
            .Include(q => q.Client)
            .Include(q => q.Property)
            .Include(q => q.MaintenanceInterval)
            .FirstOrDefaultAsync(q => q.ClientToken == token, ct);
    }

    public async Task<ProposalCreateResult> CreateAsync(CreateProposalVm vm, int clientId, int? siteId, CancellationToken ct = default)
    {
        var title = vm.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return new(ProposalCreateStatus.ValidationError, ErrorMessage: "Title is required.");

        var clientExists = await _db.Clients.AnyAsync(c => c.Id == clientId, ct);
        if (!clientExists)
            return new(ProposalCreateStatus.ClientNotFound, ErrorMessage: "Client not found.");

        var quoteNumber = await GenerateQuoteNumberAsync(ct);

        var expiryUtc = vm.ExpiryDate.HasValue
            ? DateTime.SpecifyKind(vm.ExpiryDate.Value, DateTimeKind.Utc)
            : (DateTime?)null;

        var quote = new Proposal
        {
            ClientId = clientId,
            PropertyId = siteId,
            ServiceOfferingId = vm.ServiceOfferingId,
            QuoteNumber = quoteNumber,
            Title = title,
            Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
            QuoteType = vm.QuoteType,
            MaintenanceIntervalId = vm.QuoteType == ProposalType.Routine ? vm.MaintenanceIntervalId : null,
            Amount = vm.Amount,
            Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            ExpiryDate = vm.QuoteType == ProposalType.OneTime ? expiryUtc : null,
            Status = ProposalStatus.Draft,
            CreatedUtc = DateTime.UtcNow
        };

        _db.Proposals.Add(quote);
        await _db.SaveChangesAsync(ct);

        return new(ProposalCreateStatus.Success, QuoteId: quote.Id);
    }

    public async Task<ProposalUpdateResult> UpdateAsync(int id, UpdateProposalRequest request, CancellationToken ct = default)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return new(ProposalUpdateStatus.ValidationError, ErrorMessage: "Title is required.");

        var quote = await _db.Proposals.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
            return new(ProposalUpdateStatus.NotFound);

        quote.Title = title;
        quote.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        quote.Status = request.Status;
        quote.QuoteType = request.QuoteType;
        quote.MaintenanceIntervalId = request.QuoteType == ProposalType.Routine ? request.MaintenanceIntervalId : null;
        quote.Amount = request.Amount;
        quote.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        quote.ExpiryDate = request.QuoteType == ProposalType.OneTime && request.ExpiryDate.HasValue
            ? DateTime.SpecifyKind(request.ExpiryDate.Value, DateTimeKind.Utc)
            : null;
        quote.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return new(ProposalUpdateStatus.Success);
    }

    public async Task<ProposalDeleteResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.Proposals.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
            return new(ProposalDeleteStatus.NotFound);

        _db.Proposals.Remove(quote);
        await _db.SaveChangesAsync(ct);
        return new(ProposalDeleteStatus.Success);
    }

    public async Task<PrepareQuoteForSendResult> PrepareForSendAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.Proposals
            .Include(q => q.Client)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (quote is null)
            return new(PrepareQuoteForSendStatus.NotFound);

        var clientEmail = quote.Client?.PrimaryContactEmail;
        if (string.IsNullOrWhiteSpace(clientEmail))
            return new(PrepareQuoteForSendStatus.NoClientEmail,
                ErrorMessage: "This client does not have an email address. Please add one before sending.");

        // Generate token only once; preserve it on re-send
        if (quote.ClientToken is null)
            quote.ClientToken = Guid.NewGuid();

        quote.Status = ProposalStatus.Sent;
        quote.SentUtc = DateTime.UtcNow;
        quote.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new(PrepareQuoteForSendStatus.Success,
            Token: quote.ClientToken,
            ClientEmail: clientEmail,
            ClientName: quote.Client?.PrimaryContactName ?? quote.Client?.Name ?? clientEmail,
            Quote: quote);
    }

    public async Task<ProposalUpdateResult> ApproveAsync(Guid token, CancellationToken ct = default)
    {
        var quote = await _db.Proposals.FirstOrDefaultAsync(q => q.ClientToken == token, ct);
        if (quote is null)
            return new(ProposalUpdateStatus.NotFound);

        if (quote.Status != ProposalStatus.Sent)
            return new(ProposalUpdateStatus.ValidationError,
                ErrorMessage: "This quote is not available for approval.");

        quote.Status = ProposalStatus.Accepted;
        quote.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(ProposalUpdateStatus.Success);
    }

    public async Task<ProposalUpdateResult> DeclineAsync(Guid token, CancellationToken ct = default)
    {
        var quote = await _db.Proposals.FirstOrDefaultAsync(q => q.ClientToken == token, ct);
        if (quote is null)
            return new(ProposalUpdateStatus.NotFound);

        if (quote.Status != ProposalStatus.Sent)
            return new(ProposalUpdateStatus.ValidationError,
                ErrorMessage: "This quote is not available for action.");

        quote.Status = ProposalStatus.Declined;
        quote.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(ProposalUpdateStatus.Success);
    }

    private async Task<string> GenerateQuoteNumberAsync(CancellationToken ct)
    {
        var prefix = $"QUO-{DateTime.UtcNow:yyyyMM}-";
        var count = await _db.Proposals
            .Where(q => q.QuoteNumber.StartsWith(prefix))
            .CountAsync(ct);
        return $"{prefix}{(count + 1):D4}";
    }
}
