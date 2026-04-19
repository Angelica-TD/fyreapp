using System.ComponentModel.DataAnnotations;
using FyreApp.ViewModels.Sites;

namespace FyreApp.ViewModels.ServiceQuotes;

public class InlineNewSiteVm
{
    [StringLength(120)]
    public string? Name { get; set; }

    public GoogleAddressInput Google { get; set; } = new();
    public ManualAddressInput Manual { get; set; } = new();
}
