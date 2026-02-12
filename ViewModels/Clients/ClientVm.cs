using System.ComponentModel.DataAnnotations;

namespace FyreApp.ViewModels.Clients;

public class ClientVm
{
    public int ClientId { get; set; }
    public required string ClientName { get; set; }
}
