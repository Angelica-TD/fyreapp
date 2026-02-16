using FyreApp.Models;
using FyreApp.Services.Clients;

namespace FyreApp.ViewModels.Clients;

public sealed class ClientDetailsVm
{
    public required Client Client { get; init; }

    // Form payload for update
    public required UpdateClientRequest Edit { get; init; }

    public bool OpenEdit { get; init; }
}
