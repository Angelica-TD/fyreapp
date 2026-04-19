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
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<ServiceQuoteCreateResult> CreateAsync(CreateServiceQuoteVm vm, CancellationToken ct = default)
    {
        var title = vm.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return new(ServiceQuoteCreateStatus.ValidationError, ErrorMessage: "Title is required.");

        var clientExists = await _db.Clients.AnyAsync(c => c.Id == vm.ClientId, ct);
        if (!clientExists)
            return new(ServiceQuoteCreateStatus.ClientNotFound, ErrorMessage: "Client not found.");

        var quoteNumber = await GenerateQuoteNumberAsync(ct);

        var quote = new ServiceQuote
        {
            ClientId = vm.ClientId,
            QuoteNumber = quoteNumber,
            Title = title,
            Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
            Amount = vm.Amount,
            Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            ExpiryDate = vm.ExpiryDate.HasValue ? DateTime.SpecifyKind(vm.ExpiryDate.Value, DateTimeKind.Utc) : null,
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
        quote.Amount = request.Amount;
        quote.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        quote.ExpiryDate = request.ExpiryDate.HasValue ? DateTime.SpecifyKind(request.ExpiryDate.Value, DateTimeKind.Utc) : null;
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

    private async Task<string> GenerateQuoteNumberAsync(CancellationToken ct)
    {
        var prefix = $"QUO-{DateTime.UtcNow:yyyyMM}-";
        var lastNumber = await _db.ServiceQuotes
            .Where(q => q.QuoteNumber.StartsWith(prefix))
            .CountAsync(ct);

        return $"{prefix}{(lastNumber + 1):D4}";
    }
}
