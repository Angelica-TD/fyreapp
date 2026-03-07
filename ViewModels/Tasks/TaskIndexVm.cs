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

public class TaskIndexVm
{
    public IReadOnlyList<ClientTaskListItemVm> Tasks { get; set; } = [];
    public string? StatusFilter { get; set; }
    public string? ClientFilter { get; set; }
}
