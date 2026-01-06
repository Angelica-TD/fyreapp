using Microsoft.AspNetCore.SignalR;

namespace FyreApp.Hubs;

public class ImportProgressHub : Hub
{
    public Task JoinImport(string importId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, importId);
}
