using FyreApp.Models;

namespace FyreApp.ViewModels.ServiceQuotes;

public class ServiceQuoteIndexVm
{
    public List<ServiceQuote> Quotes { get; set; } = new();
    public CreateServiceQuoteVm Create { get; set; } = new();
    public bool OpenCreateModal { get; set; }
    public List<Client> Clients { get; set; } = new();

    // Inline new client/site creation
    public bool CreateNewClient { get; set; }
    public InlineNewClientVm NewClient { get; set; } = new();
    public InlineNewSiteVm NewSite { get; set; } = new();
}
