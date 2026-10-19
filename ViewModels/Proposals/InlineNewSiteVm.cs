using System.ComponentModel.DataAnnotations;
using FyreApp.ViewModels.Properties;

namespace FyreApp.ViewModels.Proposals;

public class InlineNewSiteVm
{
    [StringLength(120)]
    public string? Name { get; set; }

    public GoogleAddressInput Google { get; set; } = new();
    public ManualAddressInput Manual { get; set; } = new();
}
