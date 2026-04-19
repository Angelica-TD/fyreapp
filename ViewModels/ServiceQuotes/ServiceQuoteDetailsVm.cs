using FyreApp.Models;

namespace FyreApp.ViewModels.ServiceQuotes;

public class ServiceQuoteDetailsVm
{
    public ServiceQuote Quote { get; set; } = null!;
    public UpdateServiceQuoteRequest Edit { get; set; } = new();
    public bool OpenEdit { get; set; }
}
