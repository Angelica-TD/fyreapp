using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.ServiceQuotes;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.ServiceQuotes;

public sealed class ServiceQuoteService : IServiceQuoteService
{
    private readonly AppDbContext _db;

    public ServiceQuoteService(AppDbContext db) => _db = db;

    public async Task<List<ServiceQuote>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.ServiceQuotes
            .Include(q => q.Client)
            .Include(q => q.Site)
            .OrderByDescending(q => q.CreatedUtc)
            .ToListAsync(ct);
    }

    public async Task<List<ServiceQuote>> GetByClientAsync(int clientId, CancellationToken ct = default)
    {
        return await _db.ServiceQuotes
            .Where(q => q.ClientId == clientId)
            .OrderByDescending(q => q.CreatedUtc)
            .ToListAsync(ct);
    }

    public async Task<ServiceQuote?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.ServiceQuotes
            .Include(q => q.Client)
            .Include(q => q.Site)
            .Include(q => q.MaintenanceInterval)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<ServiceQuote?> GetByTokenAsync(Guid token, CancellationToken ct = default)
    {
        return await _db.ServiceQuotes
            .Include(q => q.Client)
            .Include(q => q.Site)
            .Include(q => q.MaintenanceInterval)
            .FirstOrDefaultAsync(q => q.ClientToken == token, ct);
    }

    public async Task<ServiceQuoteCreateResult> CreateAsync(CreateServiceQuoteVm vm, int clientId, int? siteId, CancellationToken ct = default)
    {
        var title = vm.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return new(ServiceQuoteCreateStatus.ValidationError, ErrorMessage: "Title is required.");

        var clientExists = await _db.Clients.AnyAsync(c => c.Id == clientId, ct);
        if (!clientExists)
            return new(ServiceQuoteCreateStatus.ClientNotFound, ErrorMessage: "Client not found.");

        var quoteNumber = await GenerateQuoteNumberAsync(ct);

        var expiryUtc = vm.ExpiryDate.HasValue
            ? DateTime.SpecifyKind(vm.ExpiryDate.Value, DateTimeKind.Utc)
            : (DateTime?)null;

        var quote = new ServiceQuote
        {
            ClientId = clientId,
            SiteId = siteId,
            QuoteNumber = quoteNumber,
            Title = title,
            Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
            QuoteType = vm.QuoteType,
            MaintenanceIntervalId = vm.QuoteType == ServiceQuoteType.Routine ? vm.MaintenanceIntervalId : null,
            Amount = vm.Amount,
            Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            ExpiryDate = vm.QuoteType == ServiceQuoteType.OneTime ? expiryUtc : null,
            Status = ServiceQuoteStatus.Draft,
            CreatedUtc = DateTime.UtcNow
        };

        _db.ServiceQuotes.Add(quote);
        await _db.SaveChangesAsync(ct);

        return new(ServiceQuoteCreateStatus.Success, QuoteId: quote.Id);
    }

    public async Task<ServiceQuoteUpdateResult> UpdateAsync(int id, UpdateServiceQuoteRequest request, CancellationToken ct = default)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return new(ServiceQuoteUpdateStatus.ValidationError, ErrorMessage: "Title is required.");

        var quote = await _db.ServiceQuotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
            return new(ServiceQuoteUpdateStatus.NotFound);

        quote.Title = title;
        quote.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        quote.Status = request.Status;
        quote.QuoteType = request.QuoteType;
        quote.MaintenanceIntervalId = request.QuoteType == ServiceQuoteType.Routine ? request.MaintenanceIntervalId : null;
        quote.Amount = request.Amount;
        quote.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        quote.ExpiryDate = request.QuoteType == ServiceQuoteType.OneTime && request.ExpiryDate.HasValue
            ? DateTime.SpecifyKind(request.ExpiryDate.Value, DateTimeKind.Utc)
            : null;
        quote.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return new(ServiceQuoteUpdateStatus.Success);
    }

    public async Task<ServiceQuoteDeleteResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.ServiceQuotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
            return new(ServiceQuoteDeleteStatus.NotFound);

        _db.ServiceQuotes.Remove(quote);
        await _db.SaveChangesAsync(ct);
        return new(ServiceQuoteDeleteStatus.Success);
    }

    public async Task<PrepareQuoteForSendResult> PrepareForSendAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.ServiceQuotes
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

        quote.Status = ServiceQuoteStatus.Sent;
        quote.SentUtc = DateTime.UtcNow;
        quote.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new(PrepareQuoteForSendStatus.Success,
            Token: quote.ClientToken,
            ClientEmail: clientEmail,
            ClientName: quote.Client?.PrimaryContactName ?? quote.Client?.Name ?? clientEmail,
            Quote: quote);
    }

    public async Task<ServiceQuoteUpdateResult> ApproveAsync(Guid token, CancellationToken ct = default)
    {
        var quote = await _db.ServiceQuotes.FirstOrDefaultAsync(q => q.ClientToken == token, ct);
        if (quote is null)
            return new(ServiceQuoteUpdateStatus.NotFound);

        if (quote.Status != ServiceQuoteStatus.Sent)
            return new(ServiceQuoteUpdateStatus.ValidationError,
                ErrorMessage: "This quote is not available for approval.");

        quote.Status = ServiceQuoteStatus.Accepted;
        quote.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(ServiceQuoteUpdateStatus.Success);
    }

    public async Task<ServiceQuoteUpdateResult> DeclineAsync(Guid token, CancellationToken ct = default)
    {
        var quote = await _db.ServiceQuotes.FirstOrDefaultAsync(q => q.ClientToken == token, ct);
        if (quote is null)
            return new(ServiceQuoteUpdateStatus.NotFound);

        if (quote.Status != ServiceQuoteStatus.Sent)
            return new(ServiceQuoteUpdateStatus.ValidationError,
                ErrorMessage: "This quote is not available for action.");

        quote.Status = ServiceQuoteStatus.Declined;
        quote.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(ServiceQuoteUpdateStatus.Success);
    }

    private async Task<string> GenerateQuoteNumberAsync(CancellationToken ct)
    {
        var prefix = $"QUO-{DateTime.UtcNow:yyyyMM}-";
        var count = await _db.ServiceQuotes
            .Where(q => q.QuoteNumber.StartsWith(prefix))
            .CountAsync(ct);
        return $"{prefix}{(count + 1):D4}";
    }
}
