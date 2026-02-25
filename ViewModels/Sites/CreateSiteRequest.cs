using System.ComponentModel.DataAnnotations;

namespace FyreApp.ViewModels.Sites;

public sealed class CreateSiteRequest
{
    [Required]
    public int ClientId { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public GoogleAddressInput Google { get; set; } = new();
    public ManualAddressInput Manual { get; set; } = new();
}

public sealed class GoogleAddressInput
{
    [StringLength(300)]
    public string? PlaceId { get; set; }

    [StringLength(300)]
    public string? FormattedAddress { get; set; }
}

public sealed class ManualAddressInput
{
    [StringLength(200)]
    public string? AddressLine1 { get; set; }

    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [StringLength(80)]
    public string? Suburb { get; set; }

    [StringLength(20)]
    public string? State { get; set; }

    [StringLength(10)]
    public string? Postcode { get; set; }
}