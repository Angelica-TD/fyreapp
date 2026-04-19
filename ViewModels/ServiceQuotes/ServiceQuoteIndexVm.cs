using FyreApp.Models;

namespace FyreApp.ViewModels.ServiceQuotes;

public class ServiceQuoteIndexVm
{
    public List<ServiceQuote> Quotes { get; set; } = new();
    public CreateServiceQuoteVm Create { get; set; } = new();
    public bool OpenCreateModal { get; set; }
    public List<Client> Clients { get; set; } = new();
}
