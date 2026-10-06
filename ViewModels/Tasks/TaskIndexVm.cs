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
    // Technicians for the bulk "Assign technician" action
    public IReadOnlyList<(string Id, string Name)> Techs { get; set; } = [];
}
