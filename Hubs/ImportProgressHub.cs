using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FyreApp.Hubs;

// Streams Uptick migration runs' progress to the migration page, which only Developers can open
[Authorize(Roles = "Developer")]
public class ImportProgressHub : Hub
{
    public Task JoinImport(string importId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, importId);
}
