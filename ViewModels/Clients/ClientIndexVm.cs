using FyreApp.Models;

namespace FyreApp.ViewModels.Clients;

public class ClientIndexVm
{
    public List<Client> Clients { get; set; } = new();
    public CreateClientVm Create { get; set; } = new();
    public bool OpenCreateModal { get; set; }
}
