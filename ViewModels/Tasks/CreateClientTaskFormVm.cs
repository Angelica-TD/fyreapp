using Microsoft.AspNetCore.Mvc.Rendering;

namespace FyreApp.ViewModels.Tasks;

public class CreateClientTaskFormVm
{
    public CreateClientTaskVm Task { get; set; } = new();

    public List<SelectListItem> Clients { get; set; } = new();
    public List<SelectListItem> Properties { get; set; } = new();
}
