using FyreApp.Models;

namespace FyreApp.ViewModels.ServiceQuotes;

public class ServiceQuoteIndexVm
{
    public List<ServiceQuote> Quotes { get; set; } = new();
    public CreateServiceQuoteVm Create { get; set; } = new();
    public bool OpenCreateModal { get; set; }
    public List<Client> Clients { get; set; } = new();
    public List<MaintenanceInterval> Intervals { get; set; } = new();
    public List<ServiceOffering> ServiceOfferings { get; set; } = new();

    // Inline creation flags
    public bool CreateNewClient { get; set; }
    public InlineNewClientVm NewClient { get; set; } = new();
    public InlineNewSiteVm NewSite { get; set; } = new();
}
