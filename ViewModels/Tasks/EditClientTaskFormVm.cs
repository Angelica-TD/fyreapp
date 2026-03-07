using Microsoft.AspNetCore.Mvc.Rendering;

namespace FyreApp.ViewModels.Tasks;

public class EditClientTaskFormVm
{
    public EditClientTaskVm Task { get; set; } = new();
    public IEnumerable<SelectListItem> Clients { get; set; } = [];
    public IEnumerable<SelectListItem> Sites { get; set; } = [];
}
