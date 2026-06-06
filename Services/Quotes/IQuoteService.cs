using FyreApp.Models;
using FyreApp.ViewModels.Quotes;

namespace FyreApp.Services.Quotes;

public interface IQuoteService
{
    Task<IReadOnlyList<QuoteListItemVm>> GetAllAsync(CancellationToken ct = default);
    Task<Quote?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<QuoteCreateResult> CreateAsync(CreateQuoteVm vm, string userId, CancellationToken ct = default);
    Task<QuoteUpdateResult> UpdateAsync(int id, EditQuoteVm vm, CancellationToken ct = default);
    Task<QuoteAcceptResult> AcceptAsync(int id, CancellationToken ct = default);
    Task<QuoteUpdateResult> DeclineAsync(int id, CancellationToken ct = default);
    Task<QuoteDeleteResult> DeleteAsync(int id, CancellationToken ct = default);
}
