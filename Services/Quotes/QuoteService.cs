using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Quotes;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Quotes;

public sealed class QuoteService : IQuoteService
{
    private readonly AppDbContext _db;

    public QuoteService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<QuoteListItemVm>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Quotes
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new QuoteListItemVm
            {
                Id = q.Id,
                QuoteNumber = q.QuoteNumber,
                ClientName = q.Client.Name,
                SiteName = q.Site.Name,
                Status = q.Status,
                LineItemCount = q.LineItems.Count,
                Total = q.LineItems.Sum(li => li.Quantity * li.UnitPrice),
                ExpiryDate = q.ExpiryDate,
                CreatedAt = q.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<Quote?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Quotes
            .Include(q => q.Client)
            .Include(q => q.Site)
            .Include(q => q.LineItems)
                .ThenInclude(li => li.Asset)
            .Include(q => q.LineItems)
                .ThenInclude(li => li.ServiceType)
            .Include(q => q.LineItems)
                .ThenInclude(li => li.RecurringInterval)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<QuoteCreateResult> CreateAsync(CreateQuoteVm vm, string userId, CancellationToken ct = default)
    {
        if (vm.ClientId is null or 0)
            return new(QuoteCreateStatus.ValidationError, Error: "Client is required.");
        if (vm.SiteId is null or 0)
            return new(QuoteCreateStatus.ValidationError, Error: "Site is required.");
        if (vm.LineItems.Count == 0)
            return new(QuoteCreateStatus.ValidationError, Error: "At least one line item is required.");

        var clientExists = await _db.Clients.AnyAsync(c => c.Id == vm.ClientId.Value, ct);
        if (!clientExists) return new(QuoteCreateStatus.ClientNotFound);

        var quoteNumber = await GenerateQuoteNumberAsync(ct);
        var expiryUtc = vm.ExpiryDate.HasValue
            ? DateTime.SpecifyKind(vm.ExpiryDate.Value, DateTimeKind.Utc)
            : (DateTime?)null;

        var quote = new Quote
        {
            QuoteNumber = quoteNumber,
            ClientId = vm.ClientId.Value,
            SiteId = vm.SiteId.Value,
            Status = QuoteStatus.Draft,
            ExpiryDate = expiryUtc,
            Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        foreach (var item in vm.LineItems)
        {
            quote.LineItems.Add(new QuoteLineItem
            {
                AssetId = item.AssetId!.Value,
                ServiceTypeId = item.ServiceTypeId!.Value,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                IsRecurring = item.IsRecurring,
                RecurringIntervalId = item.IsRecurring ? item.RecurringIntervalId : null
            });
        }

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(ct);

        return new(QuoteCreateStatus.Success, Quote: quote);
    }

    public async Task<QuoteUpdateResult> UpdateAsync(int id, EditQuoteVm vm, CancellationToken ct = default)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null) return new(QuoteUpdateStatus.NotFound);

        quote.Status = vm.Status;
        quote.ExpiryDate = vm.ExpiryDate.HasValue
            ? DateTime.SpecifyKind(vm.ExpiryDate.Value, DateTimeKind.Utc)
            : null;
        quote.Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim();

        await _db.SaveChangesAsync(ct);
        return new(QuoteUpdateStatus.Success);
    }

    public async Task<QuoteAcceptResult> AcceptAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.Quotes
            .Include(q => q.LineItems)
                .ThenInclude(li => li.ServiceType)
            .Include(q => q.LineItems)
                .ThenInclude(li => li.Asset)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (quote is null) return new(QuoteAcceptStatus.NotFound);
        if (quote.Status == QuoteStatus.Accepted) return new(QuoteAcceptStatus.AlreadyAccepted);
        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
            return new(QuoteAcceptStatus.InvalidState, Error: "Only Draft or Sent quotes can be accepted.");

        // Pre-load intervals needed for NextRunDate calculation
        var intervalIds = quote.LineItems
            .Where(li => li.IsRecurring && li.RecurringIntervalId.HasValue)
            .Select(li => li.RecurringIntervalId!.Value)
            .Distinct()
            .ToList();

        var intervals = await _db.MaintenanceIntervals
            .Where(i => intervalIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        foreach (var item in quote.LineItems)
        {
            if (item.IsRecurring && item.RecurringIntervalId.HasValue)
            {
                var months = intervals.TryGetValue(item.RecurringIntervalId.Value, out var iv)
                    ? iv.Months : 12;

                _db.MaintenanceSchedules.Add(new MaintenanceSchedule
                {
                    TargetType = ScheduleTargetType.Asset,
                    AssetId = item.AssetId,
                    SiteId = quote.SiteId,
                    MaintenanceIntervalId = item.RecurringIntervalId.Value,
                    StartDate = DateTime.UtcNow,
                    NextRunDate = DateTime.UtcNow.AddMonths(months),
                    IsActive = true
                });
            }
            else
            {
                _db.ClientTasks.Add(new ClientTask
                {
                    ClientId = quote.ClientId,
                    SiteId = quote.SiteId,
                    Title = $"{item.ServiceType.Name} — {item.Asset.Name}",
                    Status = ClientTaskStatus.Open,
                    Priority = ClientTaskPriority.Normal,
                    CreatedUtc = DateTime.UtcNow,
                    CreatedByUserId = quote.CreatedByUserId
                });
            }
        }

        quote.Status = QuoteStatus.Accepted;
        await _db.SaveChangesAsync(ct);

        return new(QuoteAcceptStatus.Success);
    }

    public async Task<QuoteUpdateResult> DeclineAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null) return new(QuoteUpdateStatus.NotFound);
        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
            return new(QuoteUpdateStatus.ValidationError, Error: "Only Draft or Sent quotes can be declined.");

        quote.Status = QuoteStatus.Declined;
        await _db.SaveChangesAsync(ct);
        return new(QuoteUpdateStatus.Success);
    }

    public async Task<QuoteDeleteResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null) return new(QuoteDeleteStatus.NotFound);

        _db.Quotes.Remove(quote);
        await _db.SaveChangesAsync(ct);
        return new(QuoteDeleteStatus.Success);
    }

    private async Task<string> GenerateQuoteNumberAsync(CancellationToken ct)
    {
        var prefix = $"Q-{DateTime.UtcNow:yyyyMM}-";
        var count = await _db.Quotes
            .Where(q => q.QuoteNumber.StartsWith(prefix))
            .CountAsync(ct);
        return $"{prefix}{(count + 1):D4}";
    }
}
