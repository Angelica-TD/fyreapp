using FyreApp.Models;
using FyreApp.Services.Quotes;

namespace FyreApp.ViewModels.Quotes;

public class QuoteIndexVm
{
    public IReadOnlyList<QuoteListItemVm> Quotes { get; set; } = [];
    public List<Client> Clients { get; set; } = new();
}
