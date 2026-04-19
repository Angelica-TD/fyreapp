using System.ComponentModel.DataAnnotations;

namespace FyreApp.ViewModels.ServiceQuotes;

public class InlineNewClientVm
{
    [StringLength(200)]
    public string? Name { get; set; }

    [StringLength(200)]
    public string? PrimaryContactName { get; set; }

    [StringLength(320), EmailAddress]
    public string? PrimaryContactEmail { get; set; }

    [StringLength(32)]
    public string? PrimaryContactMobile { get; set; }
}
