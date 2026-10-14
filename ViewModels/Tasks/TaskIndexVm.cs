using FyreApp.Models;

namespace FyreApp.ViewModels.Tasks;

public class ClientTaskListItemVm
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string AddressDisplay { get; set; } = string.Empty;
    public ClientTaskPriority Priority { get; set; }
    public ClientTaskStatus Status { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

// The Tasks page; the list itself is the React TaskSearch (/api/tasks)
public class TaskIndexVm
{
    // Technicians (Uptick users) for assigning and the technician filter
    public IReadOnlyList<(string Id, string Name)> Techs { get; set; } = [];

    // The signed-in login is linked to an Uptick user, so "My tasks" can be offered
    public bool CanFilterMine { get; set; }

    // "me" for technicians (the list opens on their tasks), else "" (any)
    public string DefaultTechnician { get; set; } = "";
}
